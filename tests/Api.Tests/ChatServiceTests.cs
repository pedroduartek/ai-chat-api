using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public class ChatServiceTests
{
    private class FakeKnowledgeBaseRepo : Api.Application.IKnowledgeBaseRepository
    {
        private readonly string _kb;
        public FakeKnowledgeBaseRepo(string kb = "") => _kb = kb;
        public Task<string> GetKnowledgeBaseAsync() => Task.FromResult(_kb);
        public Task<string> GetRelevantKnowledgeBaseAsync(string query) => Task.FromResult(_kb);
    }

    private class ThrowingKbRepo : Api.Application.IKnowledgeBaseRepository
    {
        public Task<string> GetKnowledgeBaseAsync() => Task.FromException<string>(new System.IO.IOException("KB file not found"));
        public Task<string> GetRelevantKnowledgeBaseAsync(string query) => Task.FromException<string>(new System.IO.IOException("KB file not found"));
    }

    private class FakeOllamaClient : Api.Application.IOllamaClient
    {
        private readonly string _resp;
        public string? LastEndpoint { get; private set; }
        public object? LastPayload { get; private set; }
        public FakeOllamaClient(string resp) => _resp = resp;
        public Task<string> GenerateAsync(string endpoint, object payload)
        {
            LastEndpoint = endpoint;
            LastPayload = payload;
            return Task.FromResult(_resp);
        }
        public async IAsyncEnumerable<string> StreamAsync(string endpoint, object payload, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastEndpoint = endpoint;
            LastPayload = payload;
            await Task.CompletedTask;
            yield return _resp;
        }
    }

    private class ThrowingOllamaClient : Api.Application.IOllamaClient
    {
        private readonly System.Exception _ex;
        public ThrowingOllamaClient(System.Exception ex) => _ex = ex;
        public Task<string> GenerateAsync(string endpoint, object payload) => Task.FromException<string>(_ex);
        public async IAsyncEnumerable<string> StreamAsync(string endpoint, object payload, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw _ex;
#pragma warning disable CS0162 // Unreachable code detected
            yield break;
#pragma warning restore CS0162
        }
    }

    private static ChatService BuildService(string ollamaResponse, string kb = "")
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", ChatEndpoint = "/api/chat" });
        return new ChatService(new FakeKnowledgeBaseRepo(kb), new FakeOllamaClient(ollamaResponse), opts, new ChatResponseParser(), NullLogger<ChatService>.Instance);
    }

    private static (ChatService svc, FakeOllamaClient client) BuildServiceWithClient(string ollamaResponse, string kb = "")
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", ChatEndpoint = "/api/chat" });
        var client = new FakeOllamaClient(ollamaResponse);
        var svc = new ChatService(new FakeKnowledgeBaseRepo(kb), client, opts, new ChatResponseParser(), NullLogger<ChatService>.Instance);
        return (svc, client);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsPlainText_WhenServiceReturnsPlainText()
    {
        var svc = BuildService("Plain answer from API");
        var result = await svc.GenerateAnswerAsync("hello");
        Assert.Equal("Plain answer from API", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsEmptyString_WhenResponseIsEmpty()
    {
        var svc = BuildService("");
        var result = await svc.GenerateAnswerAsync("hello");
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_WorksNormally_WhenKnowledgeBaseIsEmpty()
    {
        var svc = BuildService("Answer without KB context", kb: "");
        var result = await svc.GenerateAnswerAsync("question");
        Assert.Equal("Answer without KB context", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonLines_AndReturnsConcatenatedResponses()
    {
        var content = "{ \"response\": \"First\" }\n{ \"response\": \"Second\" }";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("question");
        Assert.Equal("FirstSecond", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonObject_WithTextField()
    {
        var content = "{\"text\": \"Answer from text field\"}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("q");
        Assert.Equal("Answer from text field", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonObject_WithAnswerField()
    {
        var content = "{\"answer\": \"Direct answer\"}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("q");
        Assert.Equal("Direct answer", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonObject_WithChoicesTextField()
    {
        var content = "{\"choices\": [{\"text\": \"Choice answer\"}]}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("q");
        Assert.Equal("Choice answer", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonObject_WithChoicesMessageContent()
    {
        var content = "{\"choices\": [{\"message\": {\"content\": \"Chat message content\"}}]}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("q");
        Assert.Equal("Chat message content", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesChatApiResponse_WithMessageContent()
    {
        var content = "{\"message\": {\"role\": \"assistant\", \"content\": \"Hello from chat API\"}}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("q");
        Assert.Equal("Hello from chat API", result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsFallback_WhenResponseContainsFallbackPhrase()
    {
        const string fallback = "I couldn't find information on this website to reply to your question.";
        var content = $"{{ \"message\": {{ \"role\": \"assistant\", \"content\": \"{fallback}\" }} }}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("obscure question");
        Assert.Equal(fallback, result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ThrowsHttpRequestException_WhenOllamaClientThrows()
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", ChatEndpoint = "/api/chat" });
        var svc = new ChatService(
            new FakeKnowledgeBaseRepo(),
            new ThrowingOllamaClient(new HttpRequestException("Ollama returned non-success status 503")),
            opts,
            new ChatResponseParser(),
            NullLogger<ChatService>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GenerateAnswerAsync("hello"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_ThrowsIOException_WhenKnowledgeBaseThrows()
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", ChatEndpoint = "/api/chat" });
        var svc = new ChatService(
            new ThrowingKbRepo(),
            new FakeOllamaClient("answer"),
            opts,
            new ChatResponseParser(),
            NullLogger<ChatService>.Instance);

        await Assert.ThrowsAsync<System.IO.IOException>(() => svc.GenerateAnswerAsync("hello"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_SendsPayloadToChatEndpoint()
    {
        var (svc, client) = BuildServiceWithClient("ok");
        await svc.GenerateAnswerAsync("hi");
        Assert.Equal("/api/chat", client.LastEndpoint);
    }

    [Fact]
    public async Task GenerateAnswerAsync_PayloadContainsModelParameters()
    {
        var (svc, client) = BuildServiceWithClient("ok");
        await svc.GenerateAnswerAsync("hi");

        // Serialize and inspect the payload to verify model params are present
        var json = System.Text.Json.JsonSerializer.Serialize(client.LastPayload);
        Assert.Contains("\"temperature\"", json);
        Assert.Contains("\"top_p\"", json);
        Assert.Contains("\"top_k\"", json);
        Assert.Contains("\"repeat_penalty\"", json);
        Assert.Contains("\"num_predict\"", json);
        Assert.Contains("\"num_ctx\"", json);
        Assert.Contains("\"messages\"", json);
        Assert.Contains("\"system\"", json);
        Assert.Contains("\"user\"", json);
    }

    [Fact]
    public async Task StreamAnswerAsync_YieldsTokens()
    {
        var svc = BuildService("streamed token");
        var tokens = new List<string>();
        await foreach (var token in svc.StreamAnswerAsync("hello"))
        {
            tokens.Add(token);
        }
        Assert.Single(tokens);
        Assert.Equal("streamed token", tokens[0]);
    }
}
