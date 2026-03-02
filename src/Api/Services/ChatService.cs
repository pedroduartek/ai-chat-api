using System.Text;
using System.Text.Json;

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

    private const string SystemPrompt =
        "You are a friendly Q&A assistant for Pedro Duarte's personal website.\n" +
        "Answer ONLY using the KNOWLEDGE BASE provided. Do NOT use external knowledge or make things up.\n" +
        "If the answer is not in the KNOWLEDGE BASE, reply with exactly this sentence and nothing else: " +
        "I couldn't find information to reply to your question.\n" +
        "Write in natural, friendly sentences — not as bullet points or comma-separated lists. Plain text only, no markdown or JSON.";

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

    private async Task<string> SendMessage(string message)
    {
        var model = "llama3.2:1b";
        var sb = new StringBuilder();
        sb.AppendLine(SystemPrompt);
        sb.AppendLine();
        var kb = await _kbRepo.GetRelevantKnowledgeBaseAsync(message);
        if (!string.IsNullOrWhiteSpace(kb))
        {
            sb.AppendLine("KNOWLEDGE BASE:");
            sb.AppendLine(kb);
            sb.AppendLine("END OF KNOWLEDGE BASE");
        }
        sb.AppendLine();
        sb.Append("User question: ");
        sb.AppendLine(message);
        sb.AppendLine();
        sb.Append("Answer:");

        var payload = new { model, prompt = sb.ToString(), stream = false };
        var content = await _ollamaClient.GenerateAsync(_options.GenerateEndpoint, payload);
        return content;
    }
}
