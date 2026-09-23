using FamilyAssistant.Core.Summary;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using Xunit;

public class DeliveryTests
{
    [Fact]
    public async Task QueueFreezesPayloadAndRecipientAndSurvivesRestart()
    {
        using var f = new Fixture();
        var queue = new DeliveryQueue(f.Config);
        await queue.Enqueue(Draft(), "group@g.us", f.Now.AddMinutes(30));
        var changed = Draft(); changed.Text = "changed";
        await queue.Enqueue(changed, "other@g.us", f.Now.AddMinutes(30));
        var restarted = new DeliveryQueue(f.Config);
        var row = await restarted.Claim(f.Now);
        Assert.NotNull(row);
        Assert.Equal("Plan", row.Text);
        Assert.Equal("group@g.us", row.GroupId);
        Assert.Null(await restarted.Claim(f.Now));
        var retry = await new DeliveryQueue(f.Config).Claim(f.Now.AddSeconds(91));
        Assert.Equal(row.Id, retry!.Id);
        Assert.Equal(2, retry.Attempts);
    }
    [Fact]
    public async Task LostResponseRetriesSameIdAndStopsAfterSent()
    {
        using var f = new Fixture();
        var queue = new DeliveryQueue(f.Config);
        await queue.Enqueue(Draft(), "group@g.us", f.Now.AddMinutes(30));
        var sender = new FakeSender();
        var clock = new Clock(f.Now);
        var dispatch = new DeliveryDispatcher(queue, sender, clock);
        await dispatch.Run();
        clock.Now = f.Now.AddMinutes(1);
        await dispatch.Run();
        clock.Now = f.Now.AddMinutes(3);
        await dispatch.Run();
        Assert.Equal(2, sender.Ids.Count);
        Assert.Equal(sender.Ids[0], sender.Ids[1]);
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("blocked")]
    public async Task TerminalDeliveryIsNeverRetried(string status)
    {
        using var f = new Fixture();
        var queue = new DeliveryQueue(f.Config);
        await queue.Enqueue(Draft(), "g", f.Now.AddMinutes(30));
        var row = await queue.Claim(f.Now);
        await queue.Finish(row!.Id, status, f.Now);
        Assert.Null(await queue.Claim(f.Now.AddMinutes(10)));
    }
    [Fact]
    public async Task ExpiredMessageAndManualDraftCannotBeSent()
    {
        using var f = new Fixture();
        var queue = new DeliveryQueue(f.Config);
        await queue.Enqueue(Draft(), "g", f.Now.AddSeconds(-1));
        Assert.Null(await queue.Claim(f.Now));
        var manual = Draft(); manual.Kind = "manual";
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.Enqueue(manual, "g", f.Now.AddMinutes(1)));
    }
    [Fact]
    public async Task MismatchedGroupNeverReceivesPost()
    {
        using var f = new Fixture();
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var sender = new SummarySender(new Clients(client), f.Config);
        Assert.Equal("blocked", await sender.Send(new() { GroupId = "expected@g.us" }, default));
        Assert.Equal(0, handler.Posts);
    }
    private static SummaryDraft Draft() => new() { Id = "morning:2026-09-23", Kind = "morning", Text = "Plan" };
    private sealed class FakeSender : ISummarySender
    {
        public List<string> Ids { get; } = [];
        public Task<string> Send(Delivery delivery, CancellationToken token)
        {
            Ids.Add(delivery.Id);
            return Ids.Count == 1 ? Task.FromException<string>(new HttpRequestException()) : Task.FromResult("sent");
        }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Posts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post) Posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("""{"connection":"ready","sendEnabled":true,"groupId":"other@g.us"}""", Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "family-delivery-" + Guid.NewGuid());
        public DateTimeOffset Now => DateTimeOffset.Parse("2026-09-23T05:00:00Z");
        public IConfiguration Config { get; }
        public Fixture() => Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Summary:OutboxPath"] = Path.Combine(directory, "queue.sqlite"), ["Gateways:WhatsApp"] = "http://fake" }).Build();
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
