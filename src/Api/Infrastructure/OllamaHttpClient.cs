using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

using Api.Application;

public class OllamaHttpClient : IChatCompletionClient
{
    private readonly HttpClient _client;
    private readonly string _chatEndpoint;
    private readonly ILogger<OllamaHttpClient> _logger;

    public OllamaHttpClient(HttpClient client, IOptions<ChatOptions> options, ILogger<OllamaHttpClient> logger)
    {
        _client = client;
        _chatEndpoint = (options?.Value ?? new ChatOptions()).ChatEndpoint;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var resp = await _client.PostAsJsonAsync(_chatEndpoint, request, cancellationToken);
        var content = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama HTTP call to {Endpoint} failed with status {StatusCode} and body: {Body}", _chatEndpoint, resp.StatusCode, content);
            throw new System.Net.Http.HttpRequestException($"Ollama returned non-success status {(int)resp.StatusCode}");
        }

        return content;
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = JsonContent.Create(request)
        };

        using var resp = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Ollama streaming call to {Endpoint} failed with status {StatusCode} and body: {Body}", _chatEndpoint, resp.StatusCode, body);
            throw new System.Net.Http.HttpRequestException($"Ollama returned non-success status {(int)resp.StatusCode}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // /api/chat streaming format: { "message": { "content": "token" }, "done": false }
                if (root.TryGetProperty("message", out var msg) &&
                    msg.ValueKind == JsonValueKind.Object &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    token = content.GetString();
                }
                // /api/generate streaming format: { "response": "token" }
                else if (root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    token = r.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger?.LogDebug(ex, "Skipping malformed streaming JSON line: {Line}", line);
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
