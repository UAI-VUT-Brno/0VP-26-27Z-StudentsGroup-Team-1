using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using UAI.ZeroVP.API.Instance;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Jméno instance: jak se čte z nastavení a kde ho klient uvidí – v <c>GET /api/info</c>
/// a v hlavičce <c>X-Instance-Id</c> každé odpovědi.
/// </summary>
public sealed class InstanceIdentityTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public InstanceIdentityTests(ApiTestFactory factory) => _factory = factory;

    private static IConfiguration Configuration(string? name) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Instance:Name"] = name })
        .Build();

    private HttpClient CreateClient(string instanceName) =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting("Instance:Name", instanceName)).CreateClient();

    // --- Nastavení ----------------------------------------------------------

    [Fact]
    public void FromConfiguration_UsesConfiguredNameTrimmed()
    {
        Assert.Equal("api-5001", InstanceIdentity.FromConfiguration(Configuration("  api-5001 ")).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_WithoutName_UsesMachineNameAndProcessId(string? name)
    {
        Assert.Equal(
            $"{Environment.MachineName}-{Environment.ProcessId}",
            InstanceIdentity.FromConfiguration(Configuration(name)).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_EmptyName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new InstanceIdentity(name!));
    }

    // --- HTTP ---------------------------------------------------------------

    [Fact]
    public async Task Info_TwoInstances_ReturnTheirOwnNames()
    {
        using var clientA = CreateClient("api-a");
        using var clientB = CreateClient("api-b");

        Assert.Equal("api-a", await GetInstanceNameAsync(clientA));
        Assert.Equal("api-b", await GetInstanceNameAsync(clientB));
    }

    [Theory]
    [InlineData("/api/info", HttpStatusCode.OK)]
    [InlineData("/api/todos", HttpStatusCode.OK)]
    [InlineData("/api/todos/00000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    [InlineData("/health", HttpStatusCode.OK)]
    public async Task EveryResponse_HasInstanceHeader(string url, HttpStatusCode expectedStatusCode)
    {
        using var client = CreateClient("api-a");

        using var response = await client.GetAsync(url);

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal("api-a", Assert.Single(response.Headers.GetValues(InstanceIdentity.HeaderName)));
    }

    private static async Task<string?> GetInstanceNameAsync(HttpClient client)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/info"));
        return json.RootElement.GetProperty("instanceName").GetString();
    }
}
