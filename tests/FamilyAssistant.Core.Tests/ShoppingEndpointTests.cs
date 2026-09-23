using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class ShoppingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public ShoppingEndpointTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();
    [Theory]
    [InlineData("/shopping/import")]
    [InlineData("/shopping/biedronka/enable")]
    [InlineData("/shopping/whatsapp/proposal")]
    [InlineData("/shopping/whatsapp/list")]
    [InlineData("/shopping/deals/scan")]
    public async Task MutationsRequireFormToken(string path) => Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new StringContent("{}"))).StatusCode);
    [Fact]
    public async Task ConnectionLinksRejectForeignHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/shopping/biedronka");
        request.Headers.Host = "untrusted.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }
}
