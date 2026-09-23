using System.Net;
using System.Text.Json;
using FamilyAssistant.Core.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Xunit;

public class GoogleTests
{
    [Theory]
    [InlineData("2026-03-29", 23)]
    [InlineData("2026-10-25", 25)]
    [InlineData("2026-09-22", 24)]
    public void WarsawDaysRespectDst(string date, int hours)
    {
        var window = GoogleCalendarReader.Window(DateOnly.Parse(date), "Europe/Warsaw");
        Assert.Equal(hours, (window.End - window.Start).TotalHours);
    }

    [Fact]
    public void MapsAllDayExclusiveEndAndAliases()
    {
        using var json = JsonDocument.Parse("""{"id":"one","summary":"[Ada] Wyjazd","start":{"date":"2026-09-22"},"end":{"date":"2026-09-24"}}""");
        var result = GoogleCalendarReader.Map(json.RootElement, "calendar", [new("child", "Adrianna", ["Ada"])]);
        Assert.True(result.IsAllDay); Assert.Null(result.Start);
        Assert.Equal(new DateOnly(2026, 9, 24), result.EndDateExclusive);
        Assert.Equal(["child"], result.AssignedMembers);
    }

    [Fact]
    public void MapsTimedOffsetWithoutSubstringNameMatching()
    {
        using var json = JsonDocument.Parse("""{"id":"one","summary":"Adamin basen","start":{"dateTime":"2026-09-22T16:00:00+02:00"},"end":{"dateTime":"2026-09-22T17:00:00+02:00"},"location":"Basen"}""");
        var result = GoogleCalendarReader.Map(json.RootElement, "calendar", [new("child", "Adam", [])]);
        Assert.False(result.IsAllDay); Assert.Empty(result.AssignedMembers);
        Assert.Equal(14, result.Start!.Value.UtcDateTime.Hour); Assert.Equal("Basen", result.Location);
    }

    [Fact]
    public async Task OAuthUsesReadonlyScopePkceAndRejectsWrongStateWithoutNetwork()
    {
        using var fixture = new Fixture(_ => throw new Exception("No network expected"));
        var context = new DefaultHttpContext();
        var url = new Uri(await fixture.Auth.Begin(context));
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(GoogleAuthorization.Scope, query["scope"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("offline", query["access_type"].ToString());
        Assert.False(query.ContainsKey("client_secret"));
        var error = await Assert.ThrowsAsync<GoogleFailure>(() => fixture.Auth.Complete(context, "wrong", "fake", null));
        Assert.Equal("invalid_oauth_state", error.Code);
    }

    [Fact]
    public async Task ConsentIsOneUseAndTokensArePersistedOutsideResponses()
    {
        var calls = 0;
        using var fixture = new Fixture(request =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method);
            return Json("""{"access_token":"fake-access","refresh_token":"fake-refresh","expires_in":3600,"scope":"https://www.googleapis.com/auth/calendar.readonly"}""");
        });
        await fixture.State.Write("calendars.json", new[] { "previous-account-calendar" });
        var start = new DefaultHttpContext();
        var query = QueryHelpers.ParseQuery(new Uri(await fixture.Auth.Begin(start)).Query);
        var state = query["state"].ToString();
        var callback = new DefaultHttpContext(); callback.Request.Headers.Cookie = "family-google-state=" + state;
        await fixture.Auth.Complete(callback, state, "fake-code", null);
        Assert.Equal("authorized", await fixture.Auth.Status());
        Assert.Equal("fake-access", await fixture.Auth.AccessToken());
        Assert.Empty(await fixture.Reader.Selected());
        await Assert.ThrowsAsync<GoogleFailure>(() => fixture.Auth.Complete(callback, state, "fake-code", null));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RefreshPreservesRefreshTokenAndIsNotRepeated()
    {
        var calls = 0;
        using var fixture = new Fixture(_ => { calls++; return Json("""{"access_token":"new-access","expires_in":3600}"""); });
        await fixture.State.Write("tokens.json", new GoogleTokens("old", "keep-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal("new-access", await fixture.Auth.AccessToken());
        Assert.Equal("new-access", await fixture.Auth.AccessToken());
        Assert.Equal("keep-refresh", (await fixture.State.Read<GoogleTokens>("tokens.json"))!.RefreshToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CalendarSelectionIsValidatedAndPaginated()
    {
        var calls = 0;
        using var fixture = new Fixture(request =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            return request.RequestUri!.Query.Contains("pageToken=")
                ? Json("""{"items":[{"id":"b","summary":"B"}]}""")
                : Json("""{"items":[{"id":"a","summary":"A"}],"nextPageToken":"page2"}""");
        });
        await fixture.SeedToken();
        await fixture.Reader.Select(["a", "b", "a"]);
        Assert.Equal(["a", "b"], await fixture.Reader.Selected());
        await Assert.ThrowsAsync<GoogleFailure>(() => fixture.Reader.Select(["missing"]));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task EventsExpandRecurrenceSkipCancelledAndReportOneCalendarFailure()
    {
        using var fixture = new Fixture(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var url = request.RequestUri!;
            if (url.AbsolutePath.Contains("bad")) return new(HttpStatusCode.Forbidden);
            var query = QueryHelpers.ParseQuery(url.Query);
            Assert.Equal("true", query["singleEvents"].ToString());
            Assert.Equal("false", query["showDeleted"].ToString());
            Assert.Equal("Europe/Warsaw", query["timeZone"].ToString());
            var min = DateTimeOffset.Parse(query["timeMin"].ToString());
            var max = DateTimeOffset.Parse(query["timeMax"].ToString());
            Assert.Equal(23, (max - min).TotalHours);
            return Json("""{"items":[{"id":"skip","status":"cancelled"},{"id":"one","summary":"Event","start":{"date":"2026-03-29"},"end":{"date":"2026-03-30"}}]}""");
        });
        await fixture.SeedToken(); await fixture.State.Write("calendars.json", new[] { "good", "bad" });
        var result = await fixture.Reader.Events(new(2026, 3, 29));
        Assert.Single(result.Events); Assert.Single(result.Errors);
        Assert.Equal("calendar_access_denied", result.Errors[0].Error);
    }

    [Fact]
    public async Task UnauthorizedReadRefreshesOnceAndRetriesWithNewToken()
    {
        var calls = 0;
        using var fixture = new Fixture(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post) return Json("""{"access_token":"new","expires_in":3600}""");
            return request.Headers.Authorization!.Parameter == "fake"
                ? new(HttpStatusCode.Unauthorized) : Json("""{"items":[]}""");
        });
        await fixture.SeedToken(); Assert.Empty(await fixture.Reader.Calendars()); Assert.Equal(3, calls);
    }

    [Fact]
    public async Task RepeatedProviderFailuresOpenCircuit()
    {
        var calls = 0;
        using var fixture = new Fixture(_ => { calls++; return new(HttpStatusCode.ServiceUnavailable); });
        await fixture.SeedToken();
        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<GoogleFailure>(() => fixture.Reader.Calendars());
        var failure = await Assert.ThrowsAsync<GoogleFailure>(() => fixture.Reader.Calendars());
        Assert.Equal("calendar_circuit_open", failure.Code); Assert.Equal(9, calls);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "family-google-test-" + Guid.NewGuid());
        public GoogleState State { get; }
        public GoogleAuthorization Auth { get; }
        public GoogleCalendarReader Reader { get; }
        private readonly HttpClient client;
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            Directory.CreateDirectory(directory);
            var credentials = Path.Combine(directory, "credentials.json");
            File.WriteAllText(credentials, """{"web":{"client_id":"fake-client","client_secret":"fake-secret"}}""");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Google:StatePath"] = directory, ["Google:CredentialsPath"] = credentials }).Build();
            client = new HttpClient(new Handler(handler));
            var factory = new Factory(client);
            State = new(config); Auth = new(config, State, factory, TimeProvider.System);
            Reader = new(Auth, State, factory, config, TimeProvider.System);
        }
        public Task SeedToken() => State.Write("tokens.json", new GoogleTokens("fake", "fake-refresh", DateTimeOffset.UtcNow.AddHours(1)));
        public void Dispose() { client.Dispose(); Directory.Delete(directory, true); }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
