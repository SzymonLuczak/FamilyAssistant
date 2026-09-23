using FamilyAssistant.Core.Google;
using FamilyAssistant.Core.Summary;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using Xunit;

public class SummaryTests
{
    private static FamilySettings Settings() => new()
    {
        Family = [new() { Name = "Ada", VulcanStudentId = new string('a', 24), CalendarAliases = ["Adusia"] },
            new() { Name = "Opiekun", Type = "adult" }],
        Rules = new() { Pickups = [new() { Day = "Tuesday", Child = "Ada", PickedUpBy = "Opiekun" }] }
    };
    private static readonly DateOnly Day = new(2026, 9, 22);
    private static SchoolPlan Plan(bool stale = false, bool review = false) => new(new string('a', 24), Day,
        [new("1", "Matematyka", new(8, 0), new(8, 45), "scheduled"),
         new("2", "Polski", new(9, 0), new(9, 45), "cancelled")],
        new(8, 0), new(9, 45), review, stale, DateTimeOffset.Parse("2026-09-22T06:00:00Z"));

    [Fact]
    public void FormatterRecomputesEndIgnoringCancelledLessons()
    {
        var text = DailySummary.Format(Day, Settings(), [new("Ada", Plan())], [], []);
        Assert.Contains("08:00–08:45", text);
        Assert.DoesNotContain("09:45", text);
        Assert.Contains("odbiera Opiekun", text);
        Assert.DoesNotContain("Kalendarze", text);
    }

    [Fact]
    public void OldPlanNeverPromisesPickupTime()
    {
        var text = DailySummary.Format(Day, Settings(), [new("Ada", Plan(true))], [], []);
        Assert.Contains("godzina zakończenia niepotwierdzona", text);
        Assert.DoesNotContain("08:45", text);
    }

    [Fact]
    public void ChangedLessonKeepsTimetableHoursAndLabelsBothChangesAndCancellations()
    {
        var plan = Plan(review: true) with { Lessons = [
            new("1", "Matematyka", new(8, 0), new(8, 45), "scheduled"),
            new("2", "Polski", new(9, 0), new(9, 45), "change_requires_review"),
            new("3", "Historia", new(10, 0), new(10, 45), "cancelled")] };
        var text = DailySummary.Format(Day, Settings(), [new("Ada", plan)], [], []);
        Assert.Contains("08:00–09:45", text);
        Assert.Contains("zastępstwo / zmiana — godziny z planu", text);
        Assert.Contains("lekcja odwołana", text);
        Assert.DoesNotContain("niepotwierdzona", text);
        Assert.DoesNotContain("10:45", text);
    }

    [Fact]
    public void NoLessonsOmitsPickupSectionButMissingDataDoesNotClaimNoSchool()
    {
        var empty = DailySummary.Format(Day, Settings(), [new("Ada", Plan() with { Lessons = [] })], [], []);
        Assert.Contains("brak aktywnych zajęć", empty);
        Assert.DoesNotContain("Odbiory", empty);
        var missing = DailySummary.Format(Day, Settings(), [new("Ada", null)], [], []);
        Assert.DoesNotContain("brak aktywnych zajęć", missing);
    }

    [Fact]
    public void RulesUseTargetDayAndExplicitAliasesOnly()
    {
        var settings = Settings();
        Assert.Equal(["Ada"], DailySummary.Assigned("[Adusia] Trening", settings));
        Assert.Empty(DailySummary.Assigned("Adamin trening", settings));
        var text = DailySummary.Format(Day.AddDays(1), settings, [new("Ada", Plan())], [], []);
        Assert.DoesNotContain("Odbiory", text);
    }

    [Fact]
    public void WholeDayAndLocalTimeEventsAndLengthLimit()
    {
        FamilyCalendarEvent[] events = [new("1", "c", "Wyjazd", null, null, true, Day, Day.AddDays(1), [], null),
            new("2", "c", "Dentysta", DateTimeOffset.Parse("2026-09-22T14:00:00Z"), null, false, null, null, [], null)];
        var text = DailySummary.Format(Day, Settings(), [], events, []);
        Assert.Contains("Cały dzień — Wyjazd", text);
        Assert.Contains("16:00 — Dentysta", text);
        text = DailySummary.Format(Day, Settings(), [], [events[0] with { Title = new string('x', 6000) }], []);
        Assert.True(text.Length <= 4000);
        Assert.Contains("Podsumowanie skrócone", text);
    }

    [Theory]
    [InlineData("2026-09-22T04:59:00Z", 0)]
    [InlineData("2026-09-22T05:00:00Z", 1)]
    [InlineData("2026-09-22T05:29:00Z", 1)]
    [InlineData("2026-09-22T05:30:00Z", 0)]
    [InlineData("2026-10-25T06:00:00Z", 1)]
    [InlineData("2026-03-29T05:00:00Z", 1)]
    public void SchedulerRespectsWarsawTimeAndThirtyMinuteRecovery(string now, int count)
        => Assert.Equal(count, SummaryJob.Due(DateTimeOffset.Parse(now), Settings()).Length);

    [Fact]
    public void EveningTargetsTomorrowAcrossYearBoundary()
    {
        var slots = SummaryJob.Due(DateTimeOffset.Parse("2026-12-31T19:00:00Z"), Settings());
        Assert.Equal(("tomorrow", new DateOnly(2027, 1, 1)), Assert.Single(slots));
    }

    [Fact]
    public void InvalidRulesAreRejectedInsteadOfGuessingPeople()
    {
        var settings = Settings();
        settings.Rules.Pickups[0].PickedUpBy = "Nieznana osoba";
        Assert.Throws<SummaryFailure>(() => FamilyConfiguration.Validate(settings));
    }

    [Fact]
    public async Task SqliteDeduplicatesConcurrentWritesAndSurvivesRestart()
    {
        using var fixture = new Fixture();
        var preview = new SummaryPreview(Day, "Plan", false, DateTimeOffset.UtcNow, 1, 0, []);
        var store = new SummaryStore(fixture.Config);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.Save("morning", preview)));
        var restarted = new SummaryStore(fixture.Config);
        Assert.True(await restarted.Exists("morning", Day));
        var before = Assert.Single(await restarted.Latest());
        await restarted.Save("morning", preview with { GeneratedAt = preview.GeneratedAt.AddMinutes(1) });
        Assert.Equal(before.UpdatedAt, Assert.Single(await restarted.Latest()).UpdatedAt);
        await restarted.Save("morning", preview with { Text = "Zmieniony plan", GeneratedAt = preview.GeneratedAt.AddMinutes(2) });
        var changed = Assert.Single(await restarted.Latest());
        Assert.NotEqual(before.PayloadHash, changed.PayloadHash);
        Assert.Equal("draft", changed.Status);
        Assert.False(await restarted.Exists("morning", Day.AddDays(1)));
    }

    [Fact]
    public async Task SchoolFailureDoesNotHideCalendarAndViceVersa()
    {
        using var fixture = new Fixture();
        var sources = new FakeSources { SchoolFails = true };
        var daily = new DailySummary(new(fixture.Config), sources, TimeProvider.System);
        var first = await daily.Build(Day);
        Assert.True(first.Incomplete);
        Assert.Contains("Spotkanie", first.Text);
        Assert.Contains("brak potwierdzonego planu", first.Text);
        sources.SchoolFails = false;
        sources.CalendarFails = true;
        var second = await daily.Build(Day);
        Assert.True(second.Incomplete);
        Assert.Contains("08:00–08:45", second.Text);
        Assert.Contains("Lista wydarzeń może być niepełna", second.Text);
    }

    [Fact]
    public async Task SummaryEndpointsRequireLocalHostAndCsrf()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var page = await client.GetAsync("/summary");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/summary/preview/today", null)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/summary/history");
        request.Headers.Host = "external.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    private sealed class FakeSources : ISummarySources
    {
        public bool SchoolFails, CalendarFails;
        public Task<SchoolPlan> School(string student, DateOnly day, CancellationToken token) =>
            SchoolFails ? Task.FromException<SchoolPlan>(new Exception("private provider body")) : Task.FromResult(Plan());
        public Task<DayEvents> Calendar(DateOnly day, CancellationToken token) => CalendarFails
            ? Task.FromException<DayEvents>(new Exception("private token"))
            : Task.FromResult(new DayEvents(day, "Europe/Warsaw", [new("1", "c", "Spotkanie", null, null, true, day, day.AddDays(1), [], null)], []));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "family-summary-" + Guid.NewGuid());
        public IConfiguration Config { get; }
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "family.yaml");
            File.WriteAllText(path, """
                timezone: Europe/Warsaw
                family:
                  - name: Ada
                    type: child
                    vulcanStudentId: aaaaaaaaaaaaaaaaaaaaaaaa
                """);
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Summary:FamilyPath"] = path, ["Summary:DatabasePath"] = Path.Combine(directory, "test.sqlite") }).Build();
        }
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}
