using System.Net.Http.Json;
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
}
