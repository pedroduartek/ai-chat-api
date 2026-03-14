using System.Collections.Generic;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;
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
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _chatEndpoint = options?.Value?.ChatEndpoint ?? new ChatOptions().ChatEndpoint;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var resp = await _client.PostAsJsonAsync(_chatEndpoint, request, cancellationToken);
        var content = await resp.Content.ReadAsStringAsync();
        sw.Stop();

        if (!resp.IsSuccessStatusCode)
        {
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["ChatMode"] = "sync",
                ["Endpoint"] = _chatEndpoint,
                ["StatusCode"] = (int)resp.StatusCode,
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["ResponseLength"] = content.Length,
                ["ResponseBody"] = content
            }))
            {
                _logger.LogWarning("Ollama request failed");
            }
            throw new System.Net.Http.HttpRequestException($"Ollama returned non-success status {(int)resp.StatusCode}");
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ChatMode"] = "sync",
            ["Endpoint"] = _chatEndpoint,
            ["StatusCode"] = (int)resp.StatusCode,
            ["DurationMs"] = sw.ElapsedMilliseconds,
            ["ResponseLength"] = content.Length
        }))
        {
            _logger.LogDebug("Ollama request completed");
        }

        return content;
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = JsonContent.Create(request)
        };

        using var resp = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["ChatMode"] = "stream",
                ["Endpoint"] = _chatEndpoint,
                ["StatusCode"] = (int)resp.StatusCode,
                ["DurationMs"] = sw.ElapsedMilliseconds,
                ["ResponseLength"] = body.Length,
                ["ResponseBody"] = body
            }))
            {
                _logger.LogWarning("Ollama streaming request failed");
            }
            throw new System.Net.Http.HttpRequestException($"Ollama returned non-success status {(int)resp.StatusCode}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        var tokenCount = 0;
        var characterCount = 0;
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
            {
                var tokenText = token!;
                tokenCount++;
                characterCount += tokenText.Length;
                yield return tokenText;
            }
        }

        sw.Stop();
        _logger!.LogDebug(
            "Ollama streaming request completed for {Endpoint} with status {StatusCode} in {DurationMs}ms after {TokenCount} tokens and {StreamedCharacterCount} characters",
            _chatEndpoint,
            (int)resp.StatusCode,
            sw.ElapsedMilliseconds,
            tokenCount,
            characterCount);
    }
}
