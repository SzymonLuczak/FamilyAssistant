using System.Net;
using System.Net.Mail;
using FamilyAssistant.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class VulcanMailTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vulcan-mail-" + Guid.NewGuid());
    private sealed class Handler(Func<string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body(), System.Text.Encoding.UTF8, "application/json") });
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Outbox : IMailSender
    {
        public List<MailMessage> Sent = new();
        public bool Fail;
        public Task Send(MailMessage message, CancellationToken token)
        {
            if (Fail) throw new SmtpException("offline");
            Sent.Add(new MailMessage { Subject = message.Subject, Body = message.Body }); return Task.CompletedTask;
        }
    }
    private static string Message(string id, bool withdrawn = false) =>
        $$"""{"id":"{{id}}","subject":"Wycieczka","content":"<p>Zgoda do piątku</p>","sentAt":"2026-09-23T08:00:00+00:00","sender":"Anna Nowak","receivers":["Rodzice 3b"],"students":["Ala Łuczak"],"attachments":[{"name":"zgoda.pdf","link":"https://x.test/z"}],"withdrawn":{{(withdrawn ? "true" : "false")}}}""";

    private VulcanMailForwarder Forwarder(Func<string> body, Outbox outbox) => new(new Clients(new Handler(body)),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gateways:Vulcan"] = "http://vulcan", ["Mail:SmtpUser"] = "szymon.luczak@gmail.com", ["Mail:SmtpPassword"] = "x",
            ["Mail:Recipients"] = "joanna.luczak33@gmail.com,szymon.luczak@gmail.com", ["Mail:VulcanStatePath"] = Path.Combine(directory, "state.json") }).Build(),
        outbox, TimeProvider.System, NullLogger<VulcanMailForwarder>.Instance);

    [Fact]
    public async Task FirstRunOnlyRemembersThenForwardsNewOnce()
    {
        var body = "[" + Message("a") + "]";
        var outbox = new Outbox();
        var forwarder = Forwarder(() => body, outbox);
        await forwarder.Run();
        Assert.Empty(outbox.Sent);
        body = "[" + Message("a") + "," + Message("b") + "," + Message("c", withdrawn: true) + "]";
        await forwarder.Run(); await forwarder.Run();
        var mail = Assert.Single(outbox.Sent);
        Assert.Equal("[eduVULCAN] Wycieczka — Anna Nowak", mail.Subject);
        Assert.Contains("Ala Łuczak", mail.Body); Assert.Contains("zgoda.pdf", mail.Body);
        Assert.Equal(1, (await forwarder.Read()).ForwardedCount);
    }

    [Fact]
    public async Task FailedSendIsRetriedLater()
    {
        var body = "[]";
        var outbox = new Outbox();
        var forwarder = Forwarder(() => body, outbox);
        await forwarder.Run();
        body = "[" + Message("b") + "]";
        outbox.Fail = true; await forwarder.Run();
        Assert.NotNull((await forwarder.Read()).LastError);
        outbox.Fail = false; await forwarder.Run();
        Assert.Single(outbox.Sent);
    }

    [Fact]
    public void ComposeAddressesBothParents()
    {
        var message = new VulcanMessage("id", "Test\r\nBcc: x@y", "a & b", DateTimeOffset.UtcNow, "Szkoła", [], ["Ala"], [], false);
        using var mail = VulcanMailForwarder.Compose(message, "szymon.luczak@gmail.com", ["joanna.luczak33@gmail.com", "szymon.luczak@gmail.com"]);
        Assert.Equal(2, mail.To.Count);
        Assert.DoesNotContain("\n", mail.Subject);
        Assert.Contains("a &amp; b", mail.Body);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
