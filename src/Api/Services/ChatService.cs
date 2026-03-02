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

    private const string SystemPrompt = "You are a website Q&A assistant for Pedro Duarte.\n\nRULES (strict):\n1) Answer ONLY using the information inside the KNOWLEDGE BASE below.\n2) If the answer is not explicitly found in the KNOWLEDGE BASE, reply exactly with: \"I couldn't find information to reply to your question.\"\n3) Do NOT guess, do NOT use external knowledge, do NOT invent details.\n+4) Keep answers short, friendly, and professional.\n+5) If the user asks for contact details, you may mention Lisbon, Portugal and that the site has an About/Contact section, but do not invent new contact info.\n\nOUTPUT (mandatory):\n- Reply with plain text only. Do NOT return JSON objects, code blocks, markup, or any other wrapper — only the answer text.\n- If the answer is the fallback, respond exactly: \"I couldn't find information to reply to your question.\"";

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
        sb.AppendLine("Do not wrap your answer in JSON, code blocks, or any markup. Reply with plain text only.");
        sb.AppendLine("If the answer is not in the KNOWLEDGE BASE, reply exactly with:");
        sb.AppendLine("\"I couldn't find information to reply to your question.\"");

        var payload = new { model, prompt = sb.ToString(), stream = false };
        var content = await _ollamaClient.GenerateAsync(_options.GenerateEndpoint, payload);
        return content;
    }
}
