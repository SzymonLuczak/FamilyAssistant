using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyAssistant.Core.Shopping;

public sealed record ShoppingGatewayStatus(string Connection, bool SendEnabled, string? GroupId, string? ShoppingGroupId);
public sealed record ShoppingReply(string Id, string Text, long At);
public sealed record ShoppingProposal(string Id, long CreatedAt, string[] ProductIds);

// Proposals go to the shopping group; numeric replies there confirm products,
// and the resulting list is posted to the family board group (the summary group).
public sealed class ShoppingMessenger(ShoppingStore store, IHttpClientFactory clients, IConfiguration config, TimeProvider clock, ILogger<ShoppingMessenger> logger, LeafletScanner leaflets)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Enabled => config.GetValue<bool>("Shopping:WhatsAppEnabled");
    private string Root => (config["Gateways:WhatsApp"] ?? throw new ShoppingFailure("Brak adresu bramki WhatsApp.")).TrimEnd('/');
    private string ProposalPath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config["Shopping:DatabasePath"] ?? "/app/data/shopping.sqlite"))!, "shopping-proposal.json");
    private HttpClient Client => clients.CreateClient("whatsapp-delivery");

    public static ProductView[] Candidates(IEnumerable<ProductView> products, int size, ISet<string>? onOffer = null) => products
        .Where(p => p.Status == "Suggested" && p.PurchaseDays >= 2)
        .OrderByDescending(p => p.MayRunOut).ThenByDescending(p => onOffer?.Contains(p.Id) == true).ThenBy(p => p.SuggestedDate ?? "9999").ThenByDescending(p => p.PurchaseDays).ThenBy(p => p.Name)
        .Take(size).ToArray();

    public static string DealText(LeafletMatch deal)
    {
        var pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        var price = deal.Offer.Price is { } p ? p.ToString("0.00", pl) + " zł" : "promocja";
        var parts = new List<string> { (deal.SameProduct ? "" : "zamiennik: " + deal.Offer.Name + " ") + price };
        if (!string.IsNullOrWhiteSpace(deal.Offer.Conditions)) parts.Add(deal.Offer.Conditions!);
        if (DateOnly.TryParse(deal.Offer.ValidTo, System.Globalization.CultureInfo.InvariantCulture, out var to)) parts.Add("do " + to.ToString("dd.MM", pl));
        return "🏷 " + string.Join(", ", parts);
    }

    public static string ProposalText(ProductView[] products, IReadOnlyCollection<LeafletMatch>? deals = null)
    {
        var text = new StringBuilder("Propozycje zakupów — odpisz numerami, np. 1 3 5 albo 2-4:\n");
        for (var i = 0; i < products.Length; i++)
        {
            text.Append(i + 1).Append(". ").Append(products[i].Name).Append(products[i].MayRunOut ? " (może się kończyć)" : "");
            var deal = deals?.Where(d => d.ProductId == products[i].Id).OrderByDescending(d => d.SameProduct).ThenBy(d => d.Offer.Price ?? decimal.MaxValue).FirstOrDefault();
            if (deal is not null) text.Append(" — ").Append(DealText(deal));
            text.Append('\n');
        }
        return text.ToString().TrimEnd();
    }

    public static string ListText(IEnumerable<ProductView> products)
    {
        var items = products.Where(p => p.Status == "Confirmed").Select(p => p.Name).Order(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pl-PL"), true)).ToArray();
        return items.Length == 0 ? "Lista zakupów jest pusta." : "Lista zakupów:\n" + string.Join('\n', items.Select(n => "- " + n));
    }

    public static int[] ParseSelection(string text, int count)
    {
        var chosen = new SortedSet<int>();
        foreach (var token in text.Split(new[] { ' ', ',', ';', '.', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var range = token.Replace('\u2013', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (range.Length == 1 && int.TryParse(range[0], out var single)) chosen.Add(single);
            else if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to) && from <= to && to - from < 100)
                for (var n = from; n <= to; n++) chosen.Add(n);
        }
        return chosen.Where(n => n >= 1 && n <= count).ToArray();
    }

    private async Task<ShoppingGatewayStatus> Ready(CancellationToken token)
    {
        var status = await Client.GetFromJsonAsync<ShoppingGatewayStatus>(Root + "/status", token) ?? throw new ShoppingFailure("Bramka WhatsApp nie odpowiada.");
        if (status.Connection != "ready") throw new ShoppingFailure("WhatsApp nie jest połączony.");
        if (!status.SendEnabled) throw new ShoppingFailure("Wysyłka WhatsApp jest wyłączona (WHATSAPP_SEND_ENABLED).");
        if (string.IsNullOrEmpty(status.ShoppingGroupId)) throw new ShoppingFailure("Najpierw wybierz grupę propozycji: ./scripts/whatsapp.ps1 select-shopping-group.");
        if (string.IsNullOrEmpty(status.GroupId)) throw new ShoppingFailure("Najpierw wybierz grupę tablicy: ./scripts/whatsapp.ps1 select-group.");
        return status;
    }

    // Returns false when the gateway is busy and the send should be retried later.
    private async Task<bool> Send(string group, string id, string text, CancellationToken token)
    {
        using var response = await Client.PostAsJsonAsync(Root + "/messages/group/" + Uri.EscapeDataString(group), new { id, text }, token);
        if (response.IsSuccessStatusCode) return true;
        var body = await response.Content.ReadAsStringAsync(token);
        if (response.StatusCode == HttpStatusCode.Conflict && body.Contains("idempotency_conflict")) return true; // already delivered once; never resend
        if (response.StatusCode == HttpStatusCode.Conflict || (int)response.StatusCode >= 500) return false;
        throw new ShoppingFailure("WhatsApp odrzucił wiadomość: " + body);
    }

    public async Task<ShoppingProposal?> CurrentProposal()
    {
        try { return JsonSerializer.Deserialize<ShoppingProposal>(await File.ReadAllTextAsync(ProposalPath)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException) { return null; }
    }

    public async Task<int> SendProposal(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var status = await Ready(token);
            var deals = await leaflets.ActiveMatches();
            var products = Candidates((await store.Read()).Products, Math.Clamp(config.GetValue("Shopping:ProposalSize", 15), 1, 40), deals.Select(d => d.ProductId).ToHashSet());
            if (products.Length == 0) throw new ShoppingFailure("Brak produktów do zaproponowania (potrzeba co najmniej dwóch dni zakupów produktu).");
            var proposal = new ShoppingProposal(clock.GetUtcNow().ToString("yyyyMMddHHmmss"), clock.GetUtcNow().ToUnixTimeSeconds(), products.Select(p => p.Id).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(ProposalPath)!);
            await File.WriteAllTextAsync(ProposalPath + ".tmp", JsonSerializer.Serialize(proposal), token);
            File.Move(ProposalPath + ".tmp", ProposalPath, true);
            if (!await Send(status.ShoppingGroupId!, "shopping-proposal-" + proposal.Id, ProposalText(products, deals), token))
                throw new ShoppingFailure("Bramka WhatsApp jest zajęta. Spróbuj za chwilę.");
            return products.Length;
        }
        finally { gate.Release(); }
    }

    public async Task SendList(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var status = await Ready(token);
            if (!await Send(status.GroupId!, "shopping-list-" + clock.GetUtcNow().ToString("yyyyMMddHHmmss"), ListText((await store.Read()).Products), token))
                throw new ShoppingFailure("Bramka WhatsApp jest zajęta. Spróbuj za chwilę.");
        }
        finally { gate.Release(); }
    }

    public async Task ProcessReplies(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var replies = await Client.GetFromJsonAsync<ShoppingReply[]>(Root + "/inbox", token) ?? [];
            if (replies.Length == 0) return;
            var status = await Ready(token);
            var proposal = await CurrentProposal();
            foreach (var reply in replies.OrderBy(r => r.At))
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reply.Id)))[..20];
                int[] picked = proposal is null ? [] : ParseSelection(reply.Text, proposal.ProductIds.Length);
                if (picked.Length > 0)
                {
                    var names = new List<string>();
                    var known = (await store.Read()).Products.ToDictionary(p => p.Id);
                    foreach (var n in picked)
                        if (known.TryGetValue(proposal!.ProductIds[n - 1], out var product))
                        {
                            if (product.Status is "Suggested" or "Dismissed" or "Purchased") await store.Update(product.Id, "Confirmed", null);
                            names.Add(product.Name);
                        }
                    if (!await Send(status.GroupId!, "shopping-list-" + key, ListText((await store.Read()).Products), token)) return;
                    if (!await Send(status.ShoppingGroupId!, "shopping-ack-" + key, "Dodano: " + string.Join(", ", names) + ". Lista wysłana na tablicę.", token)) return;
                }
                using var ack = await Client.PostAsJsonAsync(Root + "/inbox/ack", new { ids = new[] { reply.Id } }, token);
                ack.EnsureSuccessStatusCode();
            }
        }
        finally { gate.Release(); }
    }

    // "Monday,Thursday" -> both days; invalid names are ignored.
    public static DayOfWeek[] ParseDays(string? value) => (value ?? "")
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(d => Enum.TryParse<DayOfWeek>(d, true, out var day) && !int.TryParse(d, out _) ? day : (DayOfWeek?)null)
        .OfType<DayOfWeek>().Distinct().ToArray();

    // Proposals on Shopping:ProposalDay (one or more days, comma separated) at ProposalTime (Europe/Warsaw); empty disables it.
    public async Task SendScheduled(CancellationToken token)
    {
        var days = ParseDays(config["Shopping:ProposalDay"]);
        if (days.Length == 0) return;
        if (!TimeOnly.TryParse(config["Shopping:ProposalTime"] ?? "18:00", System.Globalization.CultureInfo.InvariantCulture, out var time)) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        if (!days.Contains(now.DayOfWeek)) return;
        var planned = new DateTimeOffset(DateOnly.FromDateTime(now.DateTime).ToDateTime(time), zone.GetUtcOffset(now.DateTime));
        if (now < planned || now > planned.AddHours(3)) return;
        var last = await CurrentProposal();
        if (last is not null && last.CreatedAt >= planned.ToUnixTimeSeconds()) return;
        await leaflets.RunIfDue(token); // fresh leaflet deals before the proposal
        try { await SendProposal(token); }
        catch (ShoppingFailure ex) { logger.LogWarning("Nie wysłano propozycji zakupów: {Reason}", ex.Message); }
    }
}

public sealed class ShoppingWhatsAppWorker(ShoppingMessenger messenger, LeafletScanner leaflets, ILogger<ShoppingWhatsAppWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!messenger.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try { await messenger.ProcessReplies(stoppingToken); await messenger.SendScheduled(stoppingToken); await leaflets.RunIfDue(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is ShoppingFailure or HttpRequestException or TaskCanceledException or JsonException)
            { logger.LogWarning("Odpowiedzi zakupowe z WhatsApp poczekają: {Reason}", ex.Message); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
