using Quartz;

namespace FamilyAssistant.Core.Summary;

[DisallowConcurrentExecution]
public sealed class SummaryJob(FamilyConfiguration configuration, DailySummary summary, SummaryStore store,
    IConfiguration config, TimeProvider clock, ILogger<SummaryJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!config.GetValue<bool>("Summary:SchedulerEnabled")) return;
        try
        {
            var settings = configuration.Read();
            foreach (var slot in Due(clock.GetUtcNow(), settings))
            {
                if (await store.Exists(slot.Kind, slot.Day, context.CancellationToken)) continue;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(3));
                var preview = await summary.Build(slot.Day, deadline.Token);
                await store.Save(slot.Kind, preview, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
        catch { logger.LogWarning("Nie udało się przygotować zaplanowanego podglądu. Sprawdź lokalny panel."); }
    }

    public static (string Kind, DateOnly Day)[] Due(DateTimeOffset now, FamilySettings settings)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone));
        var day = DateOnly.FromDateTime(local.DateTime);
        var minute = local.Hour * 60 + local.Minute;
        var result = new List<(string, DateOnly)>();
        foreach (var (kind, time, target) in new[] {
            ("morning", settings.Notifications.MorningSummary, day),
            ("tomorrow", settings.Notifications.TomorrowSummary, day.AddDays(1)) })
        {
            var planned = TimeOnly.ParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var difference = minute - (planned.Hour * 60 + planned.Minute);
            if (difference is >= 0 and < 30) result.Add((kind, target));
        }
        return result.ToArray();
    }
}
