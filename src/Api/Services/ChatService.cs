using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.IO;

namespace Api.Services;

public class ChatService : IChatService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ChatOptions _options;
    private readonly string _kb;

    private const string SystemPrompt = "You are a website Q&A assistant for Pedro Duarte.\n\nRULES (strict):\n1) Answer ONLY using the information inside the KNOWLEDGE BASE below.\n2) If the answer is not explicitly found in the KNOWLEDGE BASE, reply exactly with: \"I can’t find that on www.pedroduartek.com.\"\n3) Do NOT guess, do NOT use external knowledge, do NOT invent details.\n4) Keep answers short, friendly, and professional.\n5) If the user asks for contact details, you may mention Lisbon, Portugal and that the site has an About/Contact section, but do not invent new contact info.\n\nOUTPUT FORMAT (mandatory):\n- Respond with a single JSON object and nothing else. The object must have a single string property named \"text\", for example: {\"text\":\"<your answer here>\"}.\n- If the answer is the fallback, set \"text\" to exactly: \"I can’t find that on www.pedroduartek.com.\"";

    public ChatService(IHttpClientFactory clientFactory, Microsoft.Extensions.Options.IOptions<ChatOptions> options)
    {
        _clientFactory = clientFactory;
        _options = options?.Value ?? new ChatOptions();
        try
        {
            var kbPath = Path.Combine(AppContext.BaseDirectory, "Resources", "website_kb.txt");
            if (File.Exists(kbPath))
            {
                // The KB file is newline-delimited JSON objects. Parse each line and extract the `text`.
                var lines = File.ReadAllLines(kbPath);
                var parts = new List<string>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            parts.Add(textProp.GetString() ?? string.Empty);
                        }
                    }
                    catch
                    {
                        // ignore malformed lines
                    }
                }

                _kb = parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
            }
            else
            {
                _kb = string.Empty;
            }
        }
        catch
        {
            _kb = string.Empty;
        }
    }

    public async Task<string> GenerateAnswerAsync(string message)
    {
        string answer = await SendMessage(message);
        return FormatAnswer(answer);
    }

    private static string FormatAnswer(string content)
    {
        string finalAnswer = content;
        try
        {
            var parts = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var parsedAny = false;
            const string fallback = "I can’t find that on www.pedroduartek.com.";

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
        var client = _clientFactory.CreateClient(_options.ClientName);
        var model = "llama3.2:1b";
        var sb = new StringBuilder();
        sb.AppendLine(SystemPrompt);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(_kb))
            sb.AppendLine(_kb);
        sb.AppendLine();
        sb.Append("User question: ");
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("Remember: if not found in the KNOWLEDGE BASE, respond exactly with:");
        sb.AppendLine("\"I can’t find that on www.pedroduartek.com.\"");

        var payload = new { model, prompt = sb.ToString(), stream = false };
        var resp = await client.PostAsJsonAsync(_options.GenerateEndpoint, payload);
        var content = await resp.Content.ReadAsStringAsync();
        return content;
    }
}
