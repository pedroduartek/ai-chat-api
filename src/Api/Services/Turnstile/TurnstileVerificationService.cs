using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Services.Turnstile;

public sealed class TurnstileVerificationService : ITurnstileVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileVerificationService> _logger;

    public TurnstileVerificationService(
        HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileVerificationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TurnstileVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp,
        string expectedAction,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return TurnstileVerificationResult.Passed();

        if (string.IsNullOrWhiteSpace(token))
        {
            return TurnstileVerificationResult.Invalid(
                "Complete the spam check before sending your message.",
                "missing-token");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var requestContent = new FormUrlEncodedContent(BuildFormPayload(token, remoteIp));

        TurnstileSiteVerifyResponse? response;
        try
        {
            response = await _httpClient
                .PostFromJsonResponseAsync<TurnstileSiteVerifyResponse>(_options.SiteVerifyUrl, requestContent, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Turnstile verification timed out");
            return TurnstileVerificationResult.ServiceFailure(
                "Spam verification is temporarily unavailable. Please try again in a moment.",
                "timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turnstile verification request failed");
            return TurnstileVerificationResult.ServiceFailure(
                "Spam verification is temporarily unavailable. Please try again in a moment.",
                "request-failed");
        }

        if (response is null)
        {
            _logger.LogWarning("Turnstile verification returned an empty response");
            return TurnstileVerificationResult.ServiceFailure(
                "Spam verification is temporarily unavailable. Please try again in a moment.",
                "empty-response");
        }

        if (!response.Success)
        {
            _logger.LogWarning(
                "Turnstile rejected token with error codes: {ErrorCodes}",
                string.Join(", ", response.ErrorCodes ?? []));
            return TurnstileVerificationResult.Invalid(
                "Spam verification failed. Please try again.",
                "siteverify-rejected",
                response.ErrorCodes ?? []);
        }

        if (!string.Equals(response.Action, expectedAction, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Turnstile action mismatch. Expected {ExpectedAction}, got {ActualAction}",
                expectedAction,
                response.Action);
            return TurnstileVerificationResult.Invalid(
                "Spam verification failed. Please refresh the page and try again.",
                "action-mismatch",
                response.ErrorCodes ?? []);
        }

        if (_options.AllowedHostnames.Length > 0 &&
            !_options.AllowedHostnames.Contains(response.Hostname ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Turnstile hostname mismatch. Expected one of {AllowedHostnames}, got {Hostname}",
                string.Join(", ", _options.AllowedHostnames),
                response.Hostname);
            return TurnstileVerificationResult.Invalid(
                "Spam verification failed. Please refresh the page and try again.",
                "hostname-mismatch",
                response.ErrorCodes ?? []);
        }

        return TurnstileVerificationResult.Passed();
    }

    private IEnumerable<KeyValuePair<string, string>> BuildFormPayload(string token, string? remoteIp)
    {
        yield return new KeyValuePair<string, string>("secret", _options.SecretKey!);
        yield return new KeyValuePair<string, string>("response", token);

        if (!string.IsNullOrWhiteSpace(remoteIp))
            yield return new KeyValuePair<string, string>("remoteip", remoteIp);
    }

    private sealed record TurnstileSiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("hostname")] string? Hostname);
}

internal static class HttpClientExtensions
{
    public static async Task<T?> PostFromJsonResponseAsync<T>(
        this HttpClient httpClient,
        string requestUri,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(requestUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
