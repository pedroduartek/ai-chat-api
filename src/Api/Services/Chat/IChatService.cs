namespace Api.Services.Chat;

public interface IChatService
{
    Task<string> GenerateAnswerAsync(string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAnswerAsync(string message, CancellationToken cancellationToken = default);
}
