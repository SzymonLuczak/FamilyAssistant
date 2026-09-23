using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class DashboardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public DashboardTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task BrowserGetsDashboardApiGetsJson()
    {
        using var html = new HttpRequestMessage(HttpMethod.Get, "/");
        html.Headers.Accept.ParseAdd("text/html");
        var page = await client.SendAsync(html);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Do kupienia", await page.Content.ReadAsStringAsync());
        Assert.Contains("\"service\"", await (await client.GetAsync("/")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DashboardRejectsForeignHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Host = "untrusted.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }
}
