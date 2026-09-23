using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class StartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public StartupTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task HealthWorksWithoutCredentialsOrGateways()
    {
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartupReportsReadOnlyIntegrations()
    {
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("read_only", document.RootElement.GetProperty("integrations").GetString());
    }

    [Theory]
    [InlineData("vulcan")]
    [InlineData("googleCalendar")]
    [InlineData("whatsapp")]
    public async Task HealthDoesNotClaimExternalConnections(string integration)
    {
        var response = await client.GetAsync("/health/integrations");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", document.RootElement.GetProperty("core").GetString());
        Assert.Equal(integration == "googleCalendar" ? "not_configured" : "disabled", document.RootElement.GetProperty(integration).GetString());
    }

    [Theory]
    [InlineData("/auth/google")]
    [InlineData("/messages")]
    public async Task IntegrationEndpointsAreAbsent(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, null)).StatusCode);
    }
}
