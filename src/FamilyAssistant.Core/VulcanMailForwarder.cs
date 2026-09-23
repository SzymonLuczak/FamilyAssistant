using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyAssistant.Core;

public sealed record VulcanAttachment(string Name, string Link);
public sealed record VulcanMessage(string Id, string Subject, string Content, DateTimeOffset SentAt, string Sender,
    string[] Receivers, string[] Students, VulcanAttachment[] Attachments, bool Withdrawn);
public sealed class VulcanMailState
{
    public bool Initialized { get; set; }
    public List<string> Forwarded { get; set; } = new();
    public long CheckedAt { get; set; }
    public int ForwardedCount { get; set; }
    public string? LastError { get; set; }
}
public interface IMailSender { Task Send(MailMessage message, CancellationToken token); }
public sealed class SmtpMailSender(IConfiguration config) : IMailSender
{
    public async Task Send(MailMessage message, CancellationToken token)
    {
        using var client = new SmtpClient(config["Mail:SmtpHost"] ?? "smtp.gmail.com", config.GetValue("Mail:SmtpPort", 587))
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(config["Mail:SmtpUser"], config["Mail:SmtpPassword"]),
            Timeout = 30000,
        };
        await client.SendMailAsync(message, token);
    }
}

// Forwards new eduVULCAN messages by e-mail. The first successful read only marks existing
// messages as seen, so the inboxes receive messages that arrive from now on.
public sealed class VulcanMailForwarder(IHttpClientFactory clients, IConfiguration config, IMailSender sender, TimeProvider clock, ILogger<VulcanMailForwarder> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string[] Recipients => (config["Mail:Recipients"] ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    public bool Enabled => !string.IsNullOrWhiteSpace(config["Mail:SmtpUser"]) && !string.IsNullOrWhiteSpace(config["Mail:SmtpPassword"])
        && Recipients.Length > 0 && !string.IsNullOrWhiteSpace(config["Gateways:Vulcan"]);
    private string StatePath => config["Mail:VulcanStatePath"] ?? "/app/data/vulcan-mail.json";

    public async Task<VulcanMailState> Read()
    {
        try { return JsonSerializer.Deserialize<VulcanMailState>(await File.ReadAllTextAsync(StatePath), Json) ?? new(); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
    }
    private async Task Save(VulcanMailState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(StatePath))!);
        await File.WriteAllTextAsync(StatePath + ".tmp", JsonSerializer.Serialize(state, Json));
        File.Move(StatePath + ".tmp", StatePath, true);
    }

    public static MailMessage Compose(VulcanMessage message, string from, string[] to)
    {
        var html = new StringBuilder();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var sent = TimeZoneInfo.ConvertTime(message.SentAt, zone);
        html.Append("<div style=\"font:14px sans-serif;color:#555;border-bottom:1px solid #ddd;padding-bottom:8px;margin-bottom:12px\">")
            .Append("<b>Wiadomość z eduVULCAN</b><br>Od: ").Append(WebUtility.HtmlEncode(message.Sender))
            .Append("<br>Data: ").Append(sent.ToString("dd.MM.yyyy HH:mm"))
            .Append("<br>Uczeń: ").Append(WebUtility.HtmlEncode(string.Join(", ", message.Students)))
            .Append("<br>Do: ").Append(WebUtility.HtmlEncode(string.Join(", ", message.Receivers))).Append("</div>");
        // School content is HTML from the provider; mail clients sanitize it when displaying.
        html.Append(message.Content.Contains('<') ? message.Content : WebUtility.HtmlEncode(message.Content).Replace("\n", "<br>"));
        if (message.Attachments.Length > 0)
        {
            html.Append("<p><b>Załączniki:</b></p><ul>");
            foreach (var a in message.Attachments)
                html.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(a.Link)).Append("\">").Append(WebUtility.HtmlEncode(a.Name)).Append("</a></li>");
            html.Append("</ul>");
        }
        html.Append("<p style=\"font:12px sans-serif;color:#888\">Przekazane automatycznie przez Family Assistant. Odpowiadaj w eduVULCAN — odpowiedź na ten e-mail nie trafi do szkoły.</p>");
        var mail = new MailMessage { From = new MailAddress(from, "Family Assistant — eduVULCAN"), Subject = Clean($"[eduVULCAN] {message.Subject} — {message.Sender}"),
            Body = html.ToString(), IsBodyHtml = true, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        foreach (var address in to) mail.To.Add(address);
        mail.Headers.Add("X-Family-Assistant-Vulcan-Id", Clean(message.Id));
        return mail;
    }
    private static string Clean(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : clean;
    }

    public async Task<object> Run(CancellationToken token = default)
    {
        if (!Enabled) return new { skipped = "not_configured" };
        await gate.WaitAsync(token);
        try
        {
            var state = await Read();
            state.CheckedAt = clock.GetUtcNow().ToUnixTimeSeconds();
            VulcanMessage[] messages;
            try
            {
                var root = config["Gateways:Vulcan"]!.TrimEnd('/');
                using var response = await clients.CreateClient("vulcan-messages").GetAsync(root + "/messages", token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    var code = body.Contains("\"error\"") ? JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() : null;
                    state.LastError = response.StatusCode switch
                    {
                        HttpStatusCode.Conflict => "eduVULCAN nie jest połączony — zarejestruj dostęp na tej stronie.",
                        HttpStatusCode.NotFound => "Bramka eduVULCAN nie obsługuje jeszcze wiadomości — przebuduj kontenery (docker compose up --build -d).",
                        _ => $"eduVULCAN nie odpowiedział poprawnie ({(int)response.StatusCode}{(code is null ? "" : ", " + code)}).",
                    };
                    await Save(state);
                    return new { error = state.LastError };
                }
                messages = await response.Content.ReadFromJsonAsync<VulcanMessage[]>(Json, token) ?? [];
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                state.LastError = "Nie udało się pobrać wiadomości z eduVULCAN: " + ex.GetType().Name + " " + ex.Message;
                await Save(state);
                logger.LogWarning("Wiadomości eduVULCAN: {Reason}", state.LastError);
                return new { error = state.LastError };
            }
            var seen = state.Forwarded.ToHashSet();
            if (!state.Initialized)
            {
                state.Forwarded = messages.Select(m => m.Id).ToList();
                state.Initialized = true; state.LastError = null;
                await Save(state);
                return new { initialized = true, existing = messages.Length };
            }
            var sent = 0;
            foreach (var message in messages.Where(m => !seen.Contains(m.Id)).OrderBy(m => m.SentAt))
            {
                if (!message.Withdrawn)
                {
                    using var email = Compose(message, config["Mail:SmtpUser"]!, Recipients);
                    try { await sender.Send(email, token); }
                    catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
                    {
                        state.LastError = "Wysyłka e-mail nie powiodła się: " + ex.Message;
                        await Save(state);
                        logger.LogWarning("Przekazanie wiadomości eduVULCAN nie powiodło się: {Reason}", ex.Message);
                        return new { forwarded = sent, error = state.LastError };
                    }
                    sent++; state.ForwardedCount++;
                }
                state.Forwarded.Add(message.Id);
                state.Forwarded = state.Forwarded.TakeLast(5000).ToList();
                await Save(state); // mark each message right after sending: never send twice
            }
            state.LastError = null;
            await Save(state);
            return new { forwarded = sent };
        }
        finally { gate.Release(); }
    }
}

public sealed class VulcanMailWorker(VulcanMailForwarder forwarder, IConfiguration config, ILogger<VulcanMailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!forwarder.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Mail:VulcanIntervalMinutes", 15), 5, 1440)));
        do
        {
            try { await forwarder.Run(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Wiadomości eduVULCAN poczekają: {Reason}", ex.Message); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
