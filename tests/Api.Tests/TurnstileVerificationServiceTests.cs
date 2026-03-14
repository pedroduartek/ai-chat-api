using System.Net;
using System.Text;
using Api.Options;
using Api.Services.Turnstile;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public class TurnstileVerificationServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenTurnstileDisabled()
    {
        var service = BuildService(
            new TurnstileOptions { Enabled = false },
            _ => throw new InvalidOperationException("HTTP client should not be used when Turnstile is disabled."));

        var result = await service.VerifyAsync(null, null, "contact_form");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsInvalid_WhenTokenMissing()
    {
        var service = BuildService(
            new TurnstileOptions { Enabled = true, SecretKey = "secret" },
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await service.VerifyAsync(null, "127.0.0.1", "contact_form");

        Assert.False(result.Success);
        Assert.False(result.IsServiceError);
        Assert.Equal("missing-token", result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenResponseMatchesActionAndHostname()
    {
        var service = BuildService(
            new TurnstileOptions
            {
                Enabled = true,
                SecretKey = "secret",
                AllowedHostnames = ["pedroduartek.com"]
            },
            _ => JsonResponse("""
                {
                  "success": true,
                  "action": "contact_form",
                  "hostname": "pedroduartek.com"
                }
                """));

        var result = await service.VerifyAsync("token", "127.0.0.1", "contact_form");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsInvalid_WhenActionDoesNotMatch()
    {
        var service = BuildService(
            new TurnstileOptions
            {
                Enabled = true,
                SecretKey = "secret",
                AllowedHostnames = ["pedroduartek.com"]
            },
            _ => JsonResponse("""
                {
                  "success": true,
                  "action": "terminal_email",
                  "hostname": "pedroduartek.com"
                }
                """));

        var result = await service.VerifyAsync("token", "127.0.0.1", "contact_form");

        Assert.False(result.Success);
        Assert.Equal("action-mismatch", result.FailureReason);
    }

    private static TurnstileVerificationService BuildService(
        TurnstileOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder));
        return new TurnstileVerificationService(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<TurnstileVerificationService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
