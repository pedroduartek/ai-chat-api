namespace Api.Application;

public interface IChatCompletionClient
{
    Task<string> GenerateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
