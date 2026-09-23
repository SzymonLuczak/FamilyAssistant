using System.Net.Http.Json;

namespace FamilyAssistant.Core.Summary;

public sealed record WhatsAppStatus(string Connection, bool SendEnabled, string? GroupId);
public sealed record WhatsAppResult(string Status);
public interface ISummarySender { Task<string> Send(Delivery delivery, CancellationToken token); }
public sealed class SummarySender(IHttpClientFactory clients, IConfiguration config) : ISummarySender
{
    public async Task<string> Send(Delivery delivery, CancellationToken token)
    {
        var root = config["Gateways:WhatsApp"] ?? throw new InvalidOperationException();
        var client = clients.CreateClient("whatsapp-delivery");
        var status = await client.GetFromJsonAsync<WhatsAppStatus>(root.TrimEnd('/') + "/status", token);
        if (status?.GroupId != delivery.GroupId) return "blocked";
        if (!status.SendEnabled || status.Connection != "ready") return "retry";
        using var response = await client.PostAsJsonAsync(root.TrimEnd('/') + "/messages/group/" + Uri.EscapeDataString(delivery.GroupId),
            new { id = delivery.Id, text = delivery.Text }, token);
        if (!response.IsSuccessStatusCode)
            return (int)response.StatusCode is 400 or 403 or 415 ? "blocked" : "retry";
        var result = await response.Content.ReadFromJsonAsync<WhatsAppResult>(token);
        return result?.Status switch { "sent" => "sent", "unknown" or "attempting" => "unknown", "dry_run" => "blocked", _ => "retry" };
    }
}
public sealed class DeliveryDispatcher(DeliveryQueue queue, ISummarySender sender, TimeProvider clock)
{
    public async Task Run(CancellationToken token = default)
    {
        var row = await queue.Claim(clock.GetUtcNow(), token);
        if (row is null) return;
        string result;
        try { result = await sender.Send(row, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { result = "retry"; }
        await queue.Finish(row.Id, result, clock.GetUtcNow(), token);
    }
}
public sealed class DeliveryWorker(DeliveryDispatcher dispatcher, IConfiguration config, ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Summary:SendEnabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await dispatcher.Run(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Kolejka powiadomień wymaga sprawdzenia w panelu."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
