using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Api.Services;

public class ChatService : IChatService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration _config;

    public ChatService(IHttpClientFactory clientFactory, IConfiguration config)
    {
        _clientFactory = clientFactory;
        _config = config;
    }

    public async Task<string> GenerateAnswerAsync(string message)
    {
        var client = _clientFactory.CreateClient("ollama");
        var model = _config["OLLAMA_MODEL"] ?? "small-llama";
        var promptToSend = "Reply to the question shortly:" + "\n\n" + message;
        var payload = new { model, prompt = promptToSend };
        var resp = await client.PostAsJsonAsync("/api/generate", payload);
        var content = await resp.Content.ReadAsStringAsync();

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
}
