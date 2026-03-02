using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

using Api.Application;
using Api.Services;

public class OllamaHttpClient : IOllamaClient
{
    private readonly System.Net.Http.IHttpClientFactory _clientFactory;
    private readonly ChatOptions _options;

    public OllamaHttpClient(System.Net.Http.IHttpClientFactory clientFactory, IOptions<ChatOptions> options)
    {
        _clientFactory = clientFactory;
        _options = options?.Value ?? new ChatOptions();
    }

    public async Task<string> GenerateAsync(string endpoint, object payload)
    {
        var client = _clientFactory.CreateClient(_options.ClientName);
        var resp = await client.PostAsJsonAsync(endpoint, payload);
        var content = await resp.Content.ReadAsStringAsync();
        return content;
    }
}
