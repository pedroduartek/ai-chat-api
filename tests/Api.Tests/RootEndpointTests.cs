using System.Collections.Generic;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.Tests;

public class RootEndpointTests : IClassFixture<ApiApplicationFactory>
{
    private readonly HttpClient _client;

    public RootEndpointTests(ApiApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetRoot_ReturnsServiceMetadata()
    {
        var response = await _client.GetAsync("/");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RootResponse>();

        Assert.NotNull(payload);
        Assert.Equal("ai-chat-api", payload.Service);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("/health", payload.Health);
        Assert.Equal("/swagger", payload.Docs);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var payload = await _client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
    }

    private sealed record RootResponse(string Service, string Status, string Health, string? Docs);

    private sealed record HealthResponse(string Status);
}

public sealed class ApiApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Turnstile:Enabled"] = "false",
                ["Warmup:Enabled"] = "false"
            });
        });
    }
}
