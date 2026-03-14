using Api.Application;

namespace Api.Services.Chat;

public interface IChatRequestFactory
{
    Task<ChatCompletionRequest> CreateAsync(string message, bool stream, CancellationToken cancellationToken = default);
}
