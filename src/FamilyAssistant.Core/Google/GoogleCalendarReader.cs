using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyAssistant.Core.Google;

public sealed class GoogleCalendarReader(GoogleAuthorization auth, GoogleState state, IHttpClientFactory clients,
    IConfiguration config, TimeProvider clock)
{
    private int failures;
    private DateTimeOffset blockedUntil;
    public string TimeZoneId => config["Google:TimeZone"] ?? "Europe/Warsaw";
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).DateTime);

    private async Task<JsonElement> Get(string url, CancellationToken cancellation = default)
    {
        if (clock.GetUtcNow() < blockedUntil) throw new GoogleFailure("calendar_circuit_open");
        var token = await auth.AccessToken();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await clients.CreateClient("google").SendAsync(request, cancellation);
                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                    failures = 0;
                    return json.RootElement.Clone();
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                { token = await auth.AccessToken(true); continue; }
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                { await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), clock, cancellation); continue; }
                throw new GoogleFailure(response.StatusCode == HttpStatusCode.Unauthorized ? "reauthorization_required" :
                    response.StatusCode == HttpStatusCode.Forbidden ? "calendar_access_denied" : "calendar_unavailable");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (attempt < 2) continue;
            }
            catch (GoogleFailure)
            {
                if (Interlocked.Increment(ref failures) >= 3) blockedUntil = clock.GetUtcNow().AddSeconds(30);
                throw;
            }
        }
        if (Interlocked.Increment(ref failures) >= 3) blockedUntil = clock.GetUtcNow().AddSeconds(30);
        throw new GoogleFailure("calendar_unavailable");
    }

    private async Task<List<JsonElement>> Pages(string url, CancellationToken cancellation = default)
    {
        var items = new List<JsonElement>();
        var seen = new HashSet<string>();
        string? page = null;
        do
        {
            var data = await Get(page == null ? url : QueryHelpers.AddQueryString(url, "pageToken", page), cancellation);
            if (data.TryGetProperty("items", out var values)) items.AddRange(values.EnumerateArray().Select(x => x.Clone()));
            page = data.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page != null && (!seen.Add(page) || seen.Count > 100)) throw new GoogleFailure("invalid_pagination");
        } while (!string.IsNullOrEmpty(page));
        return items;
    }

    public async Task<CalendarChoice[]> Calendars() => (await Pages("https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250"))
        .Where(x => !x.TryGetProperty("deleted", out var deleted) || !deleted.GetBoolean())
        .Select(x => new CalendarChoice(x.GetProperty("id").GetString()!, Text(x, "summary") ?? "Bez nazwy", Text(x, "timeZone"))).ToArray();

    public async Task<string[]> Selected() => await state.Read<string[]>("calendars.json") ?? [];
    public async Task Select(string[] ids)
    {
        if (ids.Length is 0 or > 20 || ids.Any(string.IsNullOrWhiteSpace)) throw new GoogleFailure("select_1_to_20_calendars", 400);
        var available = (await Calendars()).Select(x => x.Id).ToHashSet();
        if (ids.Any(id => !available.Contains(id))) throw new GoogleFailure("unknown_calendar", 400);
        await state.Write("calendars.json", ids.Distinct().ToArray());
    }

    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateOnly day, string timeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (new(start, zone.GetUtcOffset(start)), new(end, zone.GetUtcOffset(end)));
    }

    public async Task<DayEvents> Events(DateOnly date, CancellationToken cancellation = default)
    {
        var ids = await Selected();
        if (ids.Length == 0) throw new GoogleFailure("no_calendars_selected", 409);
        var membersFile = config["Google:MembersPath"] ?? "/app/config/calendar-members.json";
        var members = File.Exists(membersFile)
            ? JsonSerializer.Deserialize<CalendarMember[]>(await File.ReadAllTextAsync(membersFile), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [] : [];
        var window = Window(date, TimeZoneId);
        var events = new List<FamilyCalendarEvent>();
        var errors = new List<CalendarFailure>();
        foreach (var id in ids)
        {
            try
            {
                var url = QueryHelpers.AddQueryString($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(id)}/events",
                    new Dictionary<string, string?> { ["timeMin"] = window.Start.ToString("O"), ["timeMax"] = window.End.ToString("O"),
                        ["singleEvents"] = "true", ["orderBy"] = "startTime", ["showDeleted"] = "false", ["timeZone"] = TimeZoneId, ["maxResults"] = "250" });
                foreach (var item in await Pages(url, cancellation))
                {
                    if (Text(item, "status") == "cancelled") continue;
                    events.Add(Map(item, id, members));
                }
            }
            catch (GoogleFailure error) { errors.Add(new(id, error.Code)); }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            { errors.Add(new(id, "invalid_calendar_event")); }
        }
        return new(date, TimeZoneId, events.DistinctBy(e => (e.CalendarId, e.ExternalId))
            .OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start).ToArray(), errors.ToArray());
    }

    public static FamilyCalendarEvent Map(JsonElement item, string calendarId, CalendarMember[] members)
    {
        var title = Text(item, "summary") ?? "Bez tytułu";
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        var allDay = start.TryGetProperty("date", out var startDate);
        var assigned = members.Where(m => m.CalendarAliases.Append(m.Name)
            .Where(a => !string.IsNullOrWhiteSpace(a)).Any(a => title.Contains($"[{a}]", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Id).Distinct().ToArray();
        return new(item.GetProperty("id").GetString()!, calendarId, title,
            allDay ? null : DateTimeOffset.Parse(start.GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture),
            allDay ? null : DateTimeOffset.Parse(end.GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture),
            allDay, allDay ? DateOnly.Parse(startDate.GetString()!, CultureInfo.InvariantCulture) : null,
            allDay ? DateOnly.Parse(end.GetProperty("date").GetString()!, CultureInfo.InvariantCulture) : null,
            assigned, Text(item, "location"));
    }
    private static string? Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.GetString() : null;
}
