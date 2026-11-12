using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AiPlatform.Api.IntegrationTests;

// app আদৌ বুট হয় কিনা তার প্রমাণ: DI graph resolve, migration apply, routing + versioning জীবিত।
// এটা ভাঙলে বাকি সব integration test-ও ভাঙবে — তাই সবার আগে এটাই দেখার জিনিস।
[Collection(nameof(ApiCollection))]
public class SmokeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ping_ReturnsPong_OnVersionedRoute()
    {
        var response = await _client.GetAsync("/api/v1/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pong", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
