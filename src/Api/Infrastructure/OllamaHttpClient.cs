using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure;

using Api.Application;
using Api.Services;

public class OllamaHttpClient : IOllamaClient
{
    private readonly System.Net.Http.IHttpClientFactory _clientFactory;
    private readonly ChatOptions _options;
    private readonly ILogger<OllamaHttpClient> _logger;

    public OllamaHttpClient(System.Net.Http.IHttpClientFactory clientFactory, IOptions<ChatOptions> options, ILogger<OllamaHttpClient> logger)
    {
        _clientFactory = clientFactory;
        _options = options?.Value ?? new ChatOptions();
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string endpoint, object payload)
    {
        var client = _clientFactory.CreateClient(_options.ClientName);
        var resp = await client.PostAsJsonAsync(endpoint, payload);
        var content = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama HTTP call to {Endpoint} failed with status {StatusCode} and body: {Body}", endpoint, resp.StatusCode, content);
            throw new System.Net.Http.HttpRequestException($"Ollama returned non-success status {(int)resp.StatusCode}");
        }

        return content;
    }

    public async IAsyncEnumerable<string> StreamAsync(string endpoint, object payload, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _clientFactory.CreateClient(_options.ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        using var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Ollama streaming call to {Endpoint} failed with status {StatusCode} and body: {Body}", endpoint, resp.StatusCode, body);
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
            catch (JsonException)
            {
                // Skip malformed lines
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
