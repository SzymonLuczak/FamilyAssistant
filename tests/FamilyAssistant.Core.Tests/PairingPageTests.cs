using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class PairingPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public PairingPageTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task PageHasNoCacheAndRefreshesCode()
    {
        var response = await client.GetAsync("/whatsapp/pair");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("/whatsapp/qr", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task QrWithoutGatewayIsUnavailableAndNeverCached()
    {
        var response = await client.GetAsync("/whatsapp/qr");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
