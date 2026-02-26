using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Api.Services;

public class ChatService : IChatService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ChatOptions _options;

    public ChatService(IHttpClientFactory clientFactory, Microsoft.Extensions.Options.IOptions<ChatOptions> options)
    {
        _clientFactory = clientFactory;
        _options = options?.Value ?? new ChatOptions();
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
                            sb.Append(responseProp.GetString());
                            parsedAny = true;
                        }
                        else if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(textProp.GetString());
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
        var model = _options.ModelDefault;
        var promptToSend = _options.PromptPrefix + "\n\n" + message;
        var payload = new { model, prompt = promptToSend };
        var resp = await client.PostAsJsonAsync(_options.GenerateEndpoint, payload);
        var content = await resp.Content.ReadAsStringAsync();
        return content;
    }
}
