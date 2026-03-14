using Api.Security;

namespace Api.Services.Chat;

public sealed class ChatMessageValidator
{
    public const int MaxMessageLength = 500;

    public ChatMessageValidationResult Validate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new ChatMessageValidationResult(null, "message required");

        var normalizedMessage = message.Trim();
        if (normalizedMessage.Length > MaxMessageLength)
            return new ChatMessageValidationResult(null, $"message must be {MaxMessageLength} characters or fewer");

        if (InputSanitizer.ContainsInjection(normalizedMessage))
            return new ChatMessageValidationResult(null, "Your message contains disallowed content.");

        return new ChatMessageValidationResult(normalizedMessage, null);
    }
}
