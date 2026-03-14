namespace Api.Services.Chat;

public sealed record ChatMessageValidationResult(string? Message, string? Error)
{
    public bool IsValid => Error is null;
}
