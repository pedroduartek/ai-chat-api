using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

using Api.Application;
using Microsoft.Extensions.Logging;

namespace Api.Services;

public class ChatService : IChatService
{
    private readonly IKnowledgeBaseRepository _kbRepo;
    private readonly IOllamaClient _ollamaClient;
    private readonly ChatOptions _options;
    private readonly IChatResponseParser _parser;
    private readonly ILogger<ChatService> _logger;

    private const string SystemPromptTemplate =
        "You are a friendly assistant for Pedro Duarte's personal website.\n" +
        "Answer ONLY from the WEBSITE CONTENT below. If unsure, say exactly: " +
        "I couldn't find information on this website to reply to your question.\n" +
        "Speak naturally as if you know Pedro's website. Use plain, friendly sentences.";

    public ChatService(IKnowledgeBaseRepository kbRepo, IOllamaClient ollamaClient, Microsoft.Extensions.Options.IOptions<ChatOptions> options, IChatResponseParser parser, ILogger<ChatService> logger)
    {
        _kbRepo = kbRepo;
        _ollamaClient = ollamaClient;
        _options = options?.Value ?? new ChatOptions();
        _parser = parser;
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(string message)
    {
        _logger.LogInformation("Generating answer for message of length {Length}", message.Length);
        var raw = await SendMessage(message);
        var answer = _parser.Parse(raw);
        if (answer == ChatResponseParser.Fallback)
            _logger.LogWarning("Model returned fallback response for message of length {Length}", message.Length);
        return answer;
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Streaming answer for message of length {Length}", message.Length);
        var payload = await BuildPayload(message, stream: true);

        await foreach (var token in _ollamaClient.StreamAsync(_options.ChatEndpoint, payload, cancellationToken))
        {
            yield return token;
        }
    }

    private async Task<string> SendMessage(string message)
    {
        var payload = await BuildPayload(message, stream: false);
        var content = await _ollamaClient.GenerateAsync(_options.ChatEndpoint, payload);
        return content;
    }

    private async Task<object> BuildPayload(string message, bool stream)
    {
        var kb = await _kbRepo.GetKnowledgeBaseAsync();

        var systemContent = new StringBuilder();
        systemContent.Append(SystemPromptTemplate);
        if (!string.IsNullOrWhiteSpace(kb))
        {
            systemContent.AppendLine();
            systemContent.AppendLine();
            systemContent.AppendLine("WEBSITE CONTENT:");
            systemContent.AppendLine(kb);
            systemContent.Append("END OF WEBSITE CONTENT");
        }

        var messages = new[]
        {
            new { role = "system", content = systemContent.ToString() },
            new { role = "user", content = message }
        };

        return new
        {
            model = _options.Model,
            messages,
            stream,
            options = new
            {
                temperature = _options.Temperature,
                top_p = _options.TopP,
                top_k = _options.TopK,
                repeat_penalty = _options.RepeatPenalty,
                num_predict = _options.NumPredict,
                num_ctx = _options.NumCtx
            }
        };
    }
}
