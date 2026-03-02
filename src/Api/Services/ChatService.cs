using System.Text;
using System.Text.Json;

using Api.Application;

namespace Api.Services;

public class ChatService : IChatService
{
    private readonly IKnowledgeBaseRepository _kbRepo;
    private readonly IOllamaClient _ollamaClient;
    private readonly ChatOptions _options;

    private const string SystemPrompt = "You are a website Q&A assistant for Pedro Duarte.\n\nRULES (strict):\n1) Answer ONLY using the information inside the KNOWLEDGE BASE below.\n2) If the answer is not explicitly found in the KNOWLEDGE BASE, reply exactly with: \"I couldn't find information to reply to your question.\"\n3) Do NOT guess, do NOT use external knowledge, do NOT invent details.\n+4) Keep answers short, friendly, and professional.\n+5) If the user asks for contact details, you may mention Lisbon, Portugal and that the site has an About/Contact section, but do not invent new contact info.\n\nOUTPUT (mandatory):\n- Reply with plain text only. Do NOT return JSON objects, code blocks, markup, or any other wrapper — only the answer text.\n- If the answer is the fallback, respond exactly: \"I couldn't find information to reply to your question.\"";

    public ChatService(IKnowledgeBaseRepository kbRepo, IOllamaClient ollamaClient, Microsoft.Extensions.Options.IOptions<ChatOptions> options)
    {
        _kbRepo = kbRepo;
        _ollamaClient = ollamaClient;
        _options = options?.Value ?? new ChatOptions();
    }

    public async Task<string> GenerateAnswerAsync(string message)
    {
        var answer = await SendMessage(message);
        return FormatAnswer(answer);
    }

    private static string FormatAnswer(string content)
    {
        string finalAnswer = content;
        try
        {
            const string fallback = "I couldn't find information to reply to your question.";

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                static string NormalizeText(string txt, string fallback)
                {
                    if (string.IsNullOrEmpty(txt)) return string.Empty;
                    if (txt.Contains(fallback, StringComparison.Ordinal)) return fallback;
                    return txt;
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    return NormalizeText(textProp.GetString() ?? string.Empty, fallback);
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("answer", out var answerProp))
                {
                    if (answerProp.ValueKind == JsonValueKind.String)
                    {
                        var ansStr = answerProp.GetString() ?? string.Empty;
                        try
                        {
                            using var inner = JsonDocument.Parse(ansStr);
                            var innerRoot = inner.RootElement;
                            if (innerRoot.ValueKind == JsonValueKind.Object && innerRoot.TryGetProperty("text", out var innerText) && innerText.ValueKind == JsonValueKind.String)
                                return NormalizeText(innerText.GetString() ?? string.Empty, fallback);
                        }
                        catch { }

                        return NormalizeText(ansStr, fallback);
                    }
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choicesProp) && choicesProp.ValueKind == JsonValueKind.Array && choicesProp.GetArrayLength() > 0)
                {
                    var first = choicesProp[0];
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("text", out var chText) && chText.ValueKind == JsonValueKind.String)
                    {
                        var txt = chText.GetString() ?? string.Empty;
                        try
                        {
                            using var inner = JsonDocument.Parse(txt);
                            var innerRoot = inner.RootElement;
                            if (innerRoot.ValueKind == JsonValueKind.Object && innerRoot.TryGetProperty("text", out var innerText) && innerText.ValueKind == JsonValueKind.String)
                                return NormalizeText(innerText.GetString() ?? string.Empty, fallback);
                        }
                        catch { }

                        return NormalizeText(txt, fallback);
                    }

                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.Object && messageProp.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                    {
                        var txt = contentProp.GetString() ?? string.Empty;
                        return NormalizeText(txt, fallback);
                    }
                }
            }
            catch
            {
            }

            var parts = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var parsedAny = false;

            foreach (var part in parts)
            {
                try
                {
                    using var doc = JsonDocument.Parse(part);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("response", out var responseProp) && responseProp.ValueKind == JsonValueKind.String)
                        {
                            var txt = responseProp.GetString() ?? string.Empty;
                            if (txt.Contains(fallback, StringComparison.Ordinal))
                                return fallback;
                            sb.Append(txt);
                            parsedAny = true;
                        }
                        else if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            var txt = textProp.GetString() ?? string.Empty;
                            if (txt.Contains(fallback, StringComparison.Ordinal))
                                return fallback;
                            sb.Append(txt);
                            parsedAny = true;
                        }
                    }
                }
                catch
                {
                }
            }

            if (parsedAny)
                finalAnswer = sb.ToString();
        }
        catch
        {
        }

        return finalAnswer;
    }

    private async Task<string> SendMessage(string message)
    {
        var model = "llama3.2:1b";
        var sb = new StringBuilder();
        sb.AppendLine(SystemPrompt);
        sb.AppendLine();
        var kb = await _kbRepo.GetKnowledgeBaseAsync();
        if (!string.IsNullOrWhiteSpace(kb))
            sb.AppendLine(kb);
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
