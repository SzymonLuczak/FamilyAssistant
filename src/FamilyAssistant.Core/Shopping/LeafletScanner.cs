using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FamilyAssistant.Core.Shopping;

public sealed record LeafletLink(string Id, string Title, string Url);
public sealed record LeafletOffer(string Name, decimal? Price, decimal? RegularPrice, string? Conditions, string? ValidFrom, string? ValidTo, string Leaflet, string LeafletUrl, int Page);
public sealed record LeafletMatch(string ProductId, string ProductName, LeafletOffer Offer, bool SameProduct, string Note);
public sealed class LeafletState
{
    public Dictionary<string, ScannedLeaflet> Leaflets { get; set; } = new();
    public List<LeafletMatch> Matches { get; set; } = new();
    public long CheckedAt { get; set; }
    public long MatchedAt { get; set; }
    public string? LastError { get; set; }
}
public sealed class ScannedLeaflet
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public int Pages { get; set; }
    public Dictionary<int, List<LeafletOffer>> Offers { get; set; } = new();
    public long ScannedAt { get; set; }
}

// Reads Biedronka leaflets (published only as page images) with Claude vision and
// matches the offers to products the family buys regularly.
public sealed class LeafletScanner(ShoppingStore store, IHttpClientFactory clients, IConfiguration config, TimeProvider clock, ILogger<LeafletScanner> logger)
{
    private const string Site = "https://www.biedronka.pl";
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public bool Running => gate.CurrentCount == 0;
    // Starts a scan in the background (reading a whole leaflet takes minutes).
    public bool Start()
    {
        if (!Enabled) throw new ShoppingFailure("Brak klucza Claude API (ANTHROPIC_API_KEY w .env).");
        if (Running) return false;
        _ = Task.Run(async () =>
        {
            try { await Scan(); }
            catch (Exception ex) { logger.LogWarning("Gazetki nie zostały sprawdzone: {Reason}", ex.Message); }
        });
        return true;
    }
    public bool Enabled => !string.IsNullOrWhiteSpace(config["Shopping:AnthropicApiKey"]);
    private string StatePath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config["Shopping:DatabasePath"] ?? "/app/data/shopping.sqlite"))!, "leaflets.json");
    private string Model => config["Shopping:LeafletModel"] is { Length: > 0 } m ? m : "claude-sonnet-5";
    private Regex Exclude => new(config["Shopping:LeafletExclude"] is { Length: > 0 } e ? e : "-l-oferta|home-od|znicze|hity-i-inspiracje|nie-do-wyrzucenia|najnisze-ceny", RegexOptions.IgnoreCase);

    public static LeafletLink[] ParseLeafletLinks(string html) => Regex
        .Matches(html, @"/pl/(press|pressadult),id,([a-z0-9]+),title,([a-z0-9-]+)", RegexOptions.IgnoreCase)
        .Select(m => new LeafletLink(m.Groups[2].Value, m.Groups[3].Value, $"{Site}/pl/{m.Groups[1].Value},id,{m.Groups[2].Value},title,{m.Groups[3].Value}"))
        .DistinctBy(l => l.Id).ToArray();

    public static string? ParseLeafletUuid(string html) =>
        Regex.Match(html, @"galleryLeaflet\.init\(\s*[""']([0-9a-f-]{36})[""']", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    // Claude answers with a JSON array; tolerate surrounding prose or code fences.
    public static JsonArray ParseJsonArray(string text)
    {
        var start = text.IndexOf('['); var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return new JsonArray();
        try { return JsonNode.Parse(text[start..(end + 1)]) as JsonArray ?? new JsonArray(); }
        catch (JsonException) { return new JsonArray(); }
    }

    private static decimal? Money(JsonNode? node)
    {
        if (node is null) return null;
        var text = node.ToString().Replace("zł", "").Replace(',', '.').Trim();
        return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0 && value < 100000 ? value : null;
    }
    private static int Int(JsonNode? node) => int.TryParse(node?.ToString(), out var value) ? value : -1;
    private static string? Text(JsonNode? node, int max) => node is null ? null : node.ToString().Trim() is { Length: > 0 } t ? t[..Math.Min(max, t.Length)] : null;

    public static List<LeafletOffer> ParseOffers(string text, string leaflet, string url, int page) => ParseJsonArray(text)
        .OfType<JsonObject>()
        .Select(o => new LeafletOffer(Text(o["name"], 150) ?? "", Money(o["price"]), Money(o["regular_price"]), Text(o["conditions"], 200),
            Text(o["valid_from"], 10), Text(o["valid_to"], 10), leaflet, url, page))
        .Where(o => o.Name.Length > 1).ToList();

    public static bool Active(LeafletOffer offer, DateOnly today) =>
        !DateOnly.TryParse(offer.ValidTo, System.Globalization.CultureInfo.InvariantCulture, out var to) || to >= today;

    public async Task<LeafletState> Read()
    {
        try { return JsonSerializer.Deserialize<LeafletState>(await File.ReadAllTextAsync(StatePath), Json) ?? new(); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
    }
    private async Task Save(LeafletState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await File.WriteAllTextAsync(StatePath + ".tmp", JsonSerializer.Serialize(state, Json));
        File.Move(StatePath + ".tmp", StatePath, true);
    }

    public async Task<LeafletMatch[]> ActiveMatches()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);
        return (await Read()).Matches.Where(m => Active(m.Offer, today)).ToArray();
    }

    // Runs at most every Shopping:LeafletIntervalHours (default 12).
    public async Task RunIfDue(CancellationToken token)
    {
        if (!Enabled) return;
        var state = await Read();
        var hours = Math.Clamp(config.GetValue("Shopping:LeafletIntervalHours", 12), 1, 168);
        if (clock.GetUtcNow().ToUnixTimeSeconds() - state.CheckedAt < hours * 3600) return;
        try { await Scan(token); }
        catch (Exception ex) when (ex is ShoppingFailure or HttpRequestException or TaskCanceledException or JsonException)
        { logger.LogWarning("Gazetki nie zostały sprawdzone: {Reason}", ex.Message); }
    }

    public async Task<object> Scan(CancellationToken token = default)
    {
        if (!Enabled) throw new ShoppingFailure("Brak klucza Claude API (ANTHROPIC_API_KEY w .env).");
        await gate.WaitAsync(token);
        try
        {
            var state = await Read();
            state.CheckedAt = clock.GetUtcNow().ToUnixTimeSeconds();
            var web = clients.CreateClient("biedronka");
            var links = ParseLeafletLinks(await web.GetStringAsync(Site + "/pl/gazetki", token)).Where(l => !Exclude.IsMatch(l.Title)).ToArray();
            var budget = Math.Clamp(config.GetValue("Shopping:LeafletMaxPagesPerRun", 120), 1, 500);
            var scannedPages = 0;
            foreach (var link in links)
            {
                var html = await web.GetStringAsync(link.Url, token);
                var uuid = ParseLeafletUuid(html);
                if (uuid is null) continue;
                var leaflet = state.Leaflets.TryGetValue(uuid, out var known) ? known : state.Leaflets[uuid] = new() { Title = link.Title, Url = link.Url };
                var pages = (await web.GetFromJsonAsync<JsonObject>($"https://leaflet-api.prod.biedronka.cloud/api/leaflets/{uuid}?ctx=web", token))?["images_mobile"] as JsonArray ?? new JsonArray();
                leaflet.Pages = pages.Count;
                foreach (var page in pages.OfType<JsonObject>())
                {
                    var number = Int(page["page"]);
                    var image = page["image"]?.ToString();
                    if (number < 0 || image is null || leaflet.Offers.ContainsKey(number)) continue;
                    if (scannedPages++ >= budget) break;
                    leaflet.Offers[number] = await ReadPage(web, image, link, number, token);
                    await Save(state); // keep progress if the run is interrupted
                }
                leaflet.ScannedAt = clock.GetUtcNow().ToUnixTimeSeconds();
            }
            // Forget leaflets that are no longer published.
            var current = new HashSet<string>(links.Select(l => l.Url));
            foreach (var old in state.Leaflets.Where(l => !current.Contains(l.Value.Url)).Select(l => l.Key).ToArray()) state.Leaflets.Remove(old);
            // Matching costs one larger request; repeat it only for new pages or once a day.
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (scannedPages > 0 || now - state.MatchedAt > 24 * 3600)
            {
                state.Matches = await Match(state, token);
                state.MatchedAt = now;
            }
            state.LastError = null;
            await Save(state);
            return new { leaflets = state.Leaflets.Count, pagesRead = Math.Min(scannedPages, budget), matches = state.Matches.Count };
        }
        catch (Exception ex) when (ex is HttpRequestException or ShoppingFailure)
        {
            var state = await Read(); state.LastError = ex.Message; await Save(state); throw;
        }
        finally { gate.Release(); }
    }

    private async Task<List<LeafletOffer>> ReadPage(HttpClient web, string image, LeafletLink link, int page, CancellationToken token)
    {
        using var response = await web.GetAsync(image + (image.Contains('?') ? "&" : "?") + "fmt=jpeg&qlt=85&wid=1300", token);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        var type = response.Content.Headers.ContentType?.MediaType is "image/png" or "image/webp" or "image/gif" or "image/jpeg" ? response.Content.Headers.ContentType!.MediaType! : "image/jpeg";
        var year = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).Year;
        var prompt = $$"""
            To strona gazetki promocyjnej Biedronki ("{{link.Title}}"). Wypisz wszystkie produkty spożywcze i chemię domową z ceną.
            Odpowiedz wyłącznie tablicą JSON, bez komentarza:
            [{"name": "pełna nazwa produktu z marką i gramaturą", "price": 3.99, "regular_price": 5.49, "conditions": "np. przy zakupie 2 szt., z kartą Moja Biedronka, 2+1 gratis", "valid_from": "{{year}}-09-21", "valid_to": "{{year}}-09-26"}]
            Ceny jako liczby w złotych. Daty w formacie RRRR-MM-DD, rok {{year}} jeśli nie podano. Pomiń pola, których nie ma. Pusta strona: [].
            """;
        var text = await Claude(new JsonArray(
            new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = type, ["data"] = Convert.ToBase64String(bytes) } },
            new JsonObject { ["type"] = "text", ["text"] = prompt }), 4096, token);
        return ParseOffers(text, link.Title, link.Url, page);
    }

    private async Task<List<LeafletMatch>> Match(LeafletState state, CancellationToken token)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);
        var offers = state.Leaflets.Values.SelectMany(l => l.Offers.Values.SelectMany(o => o)).Where(o => Active(o, today)).ToArray();
        var products = (await store.Read()).Products.Where(p => p.PurchaseDays >= 2 && p.Status != "Dismissed").ToArray();
        if (offers.Length == 0 || products.Length == 0) return new();
        var offerList = string.Join('\n', offers.Select((o, i) => $"{i}: {o.Name} — {o.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} zł {o.Conditions}"));
        var productList = string.Join('\n', products.Select((p, i) => $"{i}: {p.Name}"));
        var prompt = $$"""
            Rodzina regularnie kupuje produkty z listy PRODUKTY. Znajdź w OFERTY promocje na te produkty.
            same=true gdy to ten sam produkt (ta sama marka lub marka własna i podobna gramatura), same=false gdy to zamiennik tego samego rodzaju (np. inne mleko UHT 3,2%).
            Nie dopasowuj produktów innego rodzaju. Odpowiedz wyłącznie tablicą JSON:
            [{"product": 12, "offer": 3, "same": true, "note": "krótko po polsku, np. taniej o 1,50 zł"}]

            PRODUKTY:
            {{productList}}

            OFERTY:
            {{offerList}}
            """;
        var text = await Claude(new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt }), 8192, token);
        return ParseJsonArray(text).OfType<JsonObject>()
            .Select(o => (p: Int(o["product"]), f: Int(o["offer"]), same: string.Equals(o["same"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase), note: Text(o["note"], 200) ?? ""))
            .Where(m => m.p >= 0 && m.p < products.Length && m.f >= 0 && m.f < offers.Length)
            .Select(m => new LeafletMatch(products[m.p].Id, products[m.p].Name, offers[m.f], m.same, m.note))
            .DistinctBy(m => (m.ProductId, m.Offer.Name)).ToList();
    }

    private async Task<string> Claude(JsonArray content, int maxTokens, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", config["Shopping:AnthropicApiKey"]);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(new JsonObject
        {
            ["model"] = Model, ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content })
        }.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var response = await clients.CreateClient("anthropic").SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new ShoppingFailure($"Claude API odrzuciło żądanie ({(int)response.StatusCode}): {body[..Math.Min(300, body.Length)]}");
        var result = JsonNode.Parse(body)?["content"] as JsonArray;
        return string.Concat(result?.OfType<JsonObject>().Where(c => c["type"]?.ToString() == "text").Select(c => c["text"]?.ToString()) ?? Enumerable.Empty<string?>());
    }
}
