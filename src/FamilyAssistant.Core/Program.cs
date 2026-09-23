using FamilyAssistant.Core.Google;
using FamilyAssistant.Core.Summary;
using Quartz;

var builder = WebApplication.CreateBuilder(args);
// Sending requires explicit local configuration; previews remain read-only.
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<FamilyAssistant.Core.SkeletonWorker>();
builder.Services.AddHttpClient("whatsapp", client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient("whatsapp-delivery", client => client.Timeout = TimeSpan.FromSeconds(25));
builder.Logging.AddFilter("System.Net.Http.HttpClient.whatsapp-delivery", LogLevel.Warning);
builder.Services.AddHttpClient("google", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("vulcan", client => client.Timeout = TimeSpan.FromSeconds(35));
builder.Logging.AddFilter("System.Net.Http.HttpClient.vulcan", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.google", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GoogleState>();
builder.Services.AddSingleton<GoogleAuthorization>();
builder.Services.AddSingleton<GoogleCalendarReader>();
builder.Services.AddSingleton<FamilyConfiguration>();
builder.Services.AddSingleton<ISummarySources, SummarySources>();
builder.Services.AddSingleton<DailySummary>();
builder.Services.AddSingleton<SummaryStore>();
builder.Services.AddSingleton<DeliveryQueue>();
builder.Services.AddSingleton<ISummarySender, SummarySender>();
builder.Services.AddSingleton<DeliveryDispatcher>();
builder.Services.AddHostedService<DeliveryWorker>();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
if (builder.Configuration.GetValue<bool>("Summary:SchedulerEnabled"))
{
    builder.Services.AddQuartz(q =>
    {
        var key = new JobKey("daily-summary-preview");
        q.AddJob<SummaryJob>(o => o.WithIdentity(key));
        q.AddTrigger(t => t.ForJob(key).WithIdentity("summary-minute-check").StartNow()
            .WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));
    });
    builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
}
var app = builder.Build();
FamilyAssistant.Core.PairingPage.MapPairingPage(app);
app.MapGoogle();
FamilyAssistant.Core.VulcanEndpoints.MapVulcan(app);
app.MapSummary();
app.MapHealthChecks("/health");
app.MapGet("/health/integrations", async (IHttpClientFactory clients, IConfiguration config, GoogleAuthorization google) =>
{
    var whatsapp = "disabled";
    var gatewayUrl = config["Gateways:WhatsApp"];
    if (!string.IsNullOrWhiteSpace(gatewayUrl))
    {
        try
        {
            using var response = await clients.CreateClient("whatsapp").GetAsync($"{gatewayUrl.TrimEnd('/')}/status");
            response.EnsureSuccessStatusCode();
            using var status = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            whatsapp = status.RootElement.GetProperty("connection").GetString() ?? "unknown";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            whatsapp = "unavailable";
        }
    }
    return Results.Ok(new { core = "ok", vulcan = await FamilyAssistant.Core.VulcanEndpoints.Status(clients, config), googleCalendar = await google.Status(), whatsapp });
});
app.MapGet("/", () => Results.Ok(new
{
    service = "FamilyAssistant.Core",
    milestone = 5,
    integrations = app.Configuration.GetValue<bool>("Summary:SendEnabled") ? "scheduled_delivery" : "read_only",
    setup = new { summary = "/summary", google = "/google", vulcan = "/vulcan", whatsapp = "/whatsapp/pair" }
}));
app.Run();

public partial class Program { }
