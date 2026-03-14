namespace Api.Services.Turnstile;

public sealed record TurnstileVerificationResult(
    bool Success,
    bool IsServiceError,
    string UserMessage,
    string? FailureReason = null,
    IReadOnlyList<string>? ErrorCodes = null)
{
    public static TurnstileVerificationResult Passed() =>
        new(true, false, string.Empty);

    public static TurnstileVerificationResult Invalid(
        string userMessage,
        string failureReason,
        IReadOnlyList<string>? errorCodes = null) =>
        new(false, false, userMessage, failureReason, errorCodes);

    public static TurnstileVerificationResult ServiceFailure(
        string userMessage,
        string failureReason) =>
        new(false, true, userMessage, failureReason);
}
