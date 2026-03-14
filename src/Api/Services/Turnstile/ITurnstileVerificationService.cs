namespace Api.Services.Turnstile;

public interface ITurnstileVerificationService
{
    Task<TurnstileVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp,
        string expectedAction,
        CancellationToken cancellationToken = default);
}
