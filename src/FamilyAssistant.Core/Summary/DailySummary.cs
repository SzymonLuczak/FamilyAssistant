using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using FamilyAssistant.Core.Google;

namespace FamilyAssistant.Core.Summary;

public sealed record SchoolPlan(string StudentId, DateOnly Date, SchoolLesson[] Lessons,
    TimeOnly? FirstLesson, TimeOnly? LastLesson, bool RequiresReview, bool Stale, DateTimeOffset FetchedAt);
public sealed record SchoolLesson(string Id, string Subject, TimeOnly Start, TimeOnly End, string Status);
public sealed record ChildPlan(string Name, SchoolPlan? Plan);
public sealed record SummaryPreview(DateOnly Date, string Text, bool Incomplete, DateTimeOffset GeneratedAt,
    int Students, int Events, string[] Warnings);

public interface ISummarySources
{
    Task<SchoolPlan> School(string student, DateOnly day, CancellationToken token);
    Task<DayEvents> Calendar(DateOnly day, CancellationToken token);
}
public sealed class SummarySources(IHttpClientFactory clients, IConfiguration config, GoogleCalendarReader google) : ISummarySources
{
    public async Task<SchoolPlan> School(string student, DateOnly day, CancellationToken token)
    {
        var root = config["Gateways:Vulcan"] ?? throw new SummaryFailure("vulcan_not_configured");
        var plan = await clients.CreateClient("vulcan").GetFromJsonAsync<SchoolPlan>(
            $"{root.TrimEnd('/')}/students/{student}/schedule/{day:yyyy-MM-dd}", token)
            ?? throw new SummaryFailure("invalid_school_response");
        if (plan.StudentId != student || plan.Date != day || plan.Lessons is null
            || plan.Lessons.Any(l => l.End <= l.Start || l.Status is not ("scheduled" or "cancelled" or "change_requires_review")))
            throw new SummaryFailure("invalid_school_response");
        return plan;
    }
    public Task<DayEvents> Calendar(DateOnly day, CancellationToken token) => google.Events(day, token);
}

public sealed class DailySummary(FamilyConfiguration configuration, ISummarySources sources, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);

    public async Task<SummaryPreview> Build(DateOnly day, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var settings = configuration.Read();
            var warnings = new List<string>();
            var children = new List<ChildPlan>();
            // Keep the serialized gateway requests below each HTTP timeout while Google runs independently.
            var calendarTask = ReadCalendar(day, token);
            try
            {
                foreach (var person in settings.Family.Where(p => p.Enabled && p.Type == "child"))
                {
                    try { children.Add(new(person.Name, await sources.School(person.VulcanStudentId!, day, token))); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch { children.Add(new(person.Name, null)); }
                }
            }
            finally { await calendarTask; }
            var calendar = await calendarTask;
            if (calendar is null || calendar.Errors.Length > 0)
                warnings.Add("Nie udało się pobrać wszystkich kalendarzy. Lista wydarzeń może być niepełna.");
            foreach (var child in children)
            {
                if (child.Plan is null) warnings.Add($"{child.Name}: brak potwierdzonego planu szkoły.");
                else if (child.Plan.Stale) warnings.Add($"{child.Name}: stara kopia planu, pobrana {Local(child.Plan.FetchedAt):dd.MM HH:mm}. Sprawdź dziennik.");
                if (child.Plan?.RequiresReview == true || child.Plan?.Lessons.Any(l => l.Status == "change_requires_review") == true)
                    warnings.Add($"{child.Name}: pokazano godziny z planu; sprawdź szczegóły zmiany w dzienniku.");
            }
            var events = calendar?.Events ?? [];
            return new(day, Format(day, settings, children, events, warnings), warnings.Count > 0,
                clock.GetUtcNow(), children.Count, events.Length, warnings.ToArray());
        }
        finally { gate.Release(); }
    }

    private async Task<DayEvents?> ReadCalendar(DateOnly day, CancellationToken token)
    {
        try { return await sources.Calendar(day, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public static DateTimeOffset Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
    private static string Clean(string text) => Regex.Replace(text, @"[\p{C}\r\n]+", " ").Replace("*", "").Replace("_", "").Replace("`", "").Trim();
    public static string[] Assigned(string title, FamilySettings settings) => settings.Family.Where(p => p.Enabled
        && p.CalendarAliases.Append(p.Name).Where(a => !string.IsNullOrWhiteSpace(a))
            .Any(a => title.Contains($"[{a}]", StringComparison.OrdinalIgnoreCase))).Select(p => p.Name).ToArray();

    public static string Format(DateOnly day, FamilySettings settings, List<ChildPlan> children,
        FamilyCalendarEvent[] events, List<string> warnings)
    {
        var b = new StringBuilder($"👨‍👩‍👧‍👦 *Plan rodziny — {day.ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("pl-PL"))}*\n");
        if (warnings.Count > 0)
        {
            b.AppendLine("\n⚠️ *Do sprawdzenia*");
            foreach (var warning in warnings) b.AppendLine(Clean(warning));
        }
        if (children.Count > 0)
        {
            b.AppendLine("\n🏫 *Szkoła*");
            foreach (var child in children)
            {
                var p = child.Plan;
                var unavailable = p is null || p.Stale;
                var changed = p?.RequiresReview == true || p?.Lessons.Any(l => l.Status == "change_requires_review") == true;
                // Display the original timetable for unresolved changes without treating it as confirmed.
                var active = p?.Lessons.Where(l => l.Status is "scheduled" or "change_requires_review").ToArray() ?? [];
                var text = unavailable ? "godzina zakończenia niepotwierdzona" : active.Length == 0 ? "brak aktywnych zajęć"
                    : $"{active.Min(l => l.Start):HH:mm}–{active.Max(l => l.End):HH:mm}";
                var notes = new List<string>();
                if (!unavailable && changed) notes.Add("zastępstwo / zmiana — godziny z planu");
                if (!unavailable && p!.Lessons.Any(l => l.Status == "cancelled")) notes.Add("lekcja odwołana");
                if (notes.Count > 0) text += " (" + string.Join("; ", notes) + ")";
                b.AppendLine($"{Clean(child.Name)} — {text}");
            }
        }
        if (events.Length > 0)
        {
            b.AppendLine("\n📅 *Kalendarze*");
            foreach (var e in events.OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start).ThenBy(e => e.Title, StringComparer.Ordinal))
            {
                var time = e.IsAllDay ? "Cały dzień" : e.Start is not null ? Local(e.Start.Value).ToString("HH:mm") : "Godzina nieustalona";
                var members = Assigned(e.Title, settings);
                var assignment = members.Length == 0 ? "" : string.Join(", ", members.Select(Clean)) + ": ";
                b.AppendLine($"{time} — {assignment}{Clean(e.Title)}{(string.IsNullOrWhiteSpace(e.Location) ? "" : " · " + Clean(e.Location))}");
            }
        }
        var pickups = settings.Rules.Pickups.Where(r => r.Day == day.DayOfWeek.ToString()).Where(rule =>
        {
            var plan = children.FirstOrDefault(c => c.Name == rule.Child)?.Plan;
            return plan is null || plan.Stale || plan.RequiresReview || plan.Lessons.Any(l => l.Status != "cancelled");
        }).ToArray();
        if (pickups.Length > 0)
        {
            b.AppendLine("\n🚗 *Odbiory — stałe zasady*");
            foreach (var rule in pickups)
            {
                b.AppendLine($"{Clean(rule.Child)} — odbiera {Clean(rule.PickedUpBy)} (potwierdź w razie zmian).");
            }
        }
        var result = b.ToString().Trim();
        if (result.Length > 3900)
        {
            // Never silently truncate a message prepared for the gateway's 4000 character limit.
            result = result[..3800];
            if (char.IsHighSurrogate(result[^1])) result = result[..^1];
            result += "\n⚠️ Podsumowanie skrócone. Sprawdź pełne plany i kalendarze w panelach.";
        }
        return result;
    }
}
