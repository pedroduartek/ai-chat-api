using System.Runtime.CompilerServices;
using System.Collections.Generic;

using Api.Application;
using Microsoft.Extensions.Logging;

namespace Api.Services.Chat;

public class ChatService : IChatService
{
    private readonly IChatRequestFactory _requestFactory;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly IChatResponseParser _parser;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IChatRequestFactory requestFactory, IChatCompletionClient chatCompletionClient, IChatResponseParser parser, ILogger<ChatService> logger)
    {
        _requestFactory = requestFactory;
        _chatCompletionClient = chatCompletionClient;
        _parser = parser;
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(string message, CancellationToken cancellationToken = default)
    {
        var raw = await SendMessage(message, cancellationToken);
        var answer = _parser.Parse(raw);
        if (answer == ChatResponseParser.Fallback)
        {
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["Question"] = message,
                ["QuestionLength"] = message.Length
            }))
            {
                _logger.LogWarning("Model returned fallback response");
            }
        }
        return answer;
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = await _requestFactory.CreateAsync(message, stream: true, cancellationToken);

        await foreach (var token in _chatCompletionClient.StreamAsync(request, cancellationToken))
        {
            yield return token;
        }
    }

    private async Task<string> SendMessage(string message, CancellationToken cancellationToken)
    {
        var request = await _requestFactory.CreateAsync(message, stream: false, cancellationToken);
        return await _chatCompletionClient.GenerateAsync(request, cancellationToken);
    }
}
