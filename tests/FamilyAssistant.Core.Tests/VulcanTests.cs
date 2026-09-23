using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

public class VulcanTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public VulcanTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task PageIsLocalAndNotCached()
    {
        var response = await client.GetAsync("/vulcan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("eduvulcan.pl/api/ap", await response.Content.ReadAsStringAsync());
        using var external = new HttpRequestMessage(HttpMethod.Get, "/vulcan");
        external.Headers.Host = "attacker.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(external)).StatusCode);
    }

    [Fact]
    public async Task RegistrationRequiresCsrfAndDoesNotEchoContent()
    {
        var response = await client.PostAsync("/vulcan/register", new StringContent("secret-sentinel"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("secret-sentinel", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProxyRejectsInvalidStudentAndDate()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/vulcan/students/invalid/schedule/2026-09-22")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/vulcan/students/aaaaaaaaaaaaaaaaaaaaaaaa/schedule/bad-date")).StatusCode);
    }
}
