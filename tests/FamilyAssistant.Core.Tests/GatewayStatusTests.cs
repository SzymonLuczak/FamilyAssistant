using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class GatewayStatusTests
{
    [Theory]
    [InlineData("{\"connection\":\"ready\"}", false, "ready")]
    [InlineData("{\"connection\":\"qr\"}", false, "qr")]
    [InlineData("invalid json", false, "unavailable")]
    [InlineData("", true, "unavailable")]
    public async Task CoreReportsGatewayStatusWithoutLosingLiveness(string json, bool fail, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Gateways:WhatsApp"] = "http://fake-gateway" }));
            builder.ConfigureServices(services => services.AddHttpClient("whatsapp")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(json, fail)));
        });
        using var client = factory.CreateClient();
        var response = await client.GetStringAsync("/health/integrations");
        using var document = System.Text.Json.JsonDocument.Parse(response);
        Assert.Equal(expected, document.RootElement.GetProperty("whatsapp").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    private sealed class StubHandler(string json, bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (fail) throw new HttpRequestException("Simulated offline gateway");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
