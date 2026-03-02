using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Api.Services;
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
    }

    private class ThrowingKbRepo : Api.Application.IKnowledgeBaseRepository
    {
        public Task<string> GetKnowledgeBaseAsync() => Task.FromException<string>(new System.IO.IOException("KB file not found"));
    }

    private class FakeOllamaClient : Api.Application.IOllamaClient
    {
        private readonly string _resp;
        public FakeOllamaClient(string resp) => _resp = resp;
        public Task<string> GenerateAsync(string endpoint, object payload) => Task.FromResult(_resp);
    }

    private class ThrowingOllamaClient : Api.Application.IOllamaClient
    {
        private readonly System.Exception _ex;
        public ThrowingOllamaClient(System.Exception ex) => _ex = ex;
        public Task<string> GenerateAsync(string endpoint, object payload) => Task.FromException<string>(_ex);
    }

    private static ChatService BuildService(string ollamaResponse, string kb = "")
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });
        return new ChatService(new FakeKnowledgeBaseRepo(kb), new FakeOllamaClient(ollamaResponse), opts);
    }

    // ── Plain text ──────────────────────────────────────────────────────────

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

    // ── JSON NDJSON parsing ─────────────────────────────────────────────────

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

    // ── Fallback phrase ─────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsFallback_WhenResponseContainsFallbackPhrase()
    {
        const string fallback = "I couldn't find information to reply to your question.";
        var content = $"{{ \"response\": \"{fallback}\" }}";
        var svc = BuildService(content);
        var result = await svc.GenerateAnswerAsync("obscure question");
        Assert.Equal(fallback, result);
    }

    // ── Error propagation ───────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAnswerAsync_ThrowsHttpRequestException_WhenOllamaClientThrows()
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });
        var svc = new ChatService(
            new FakeKnowledgeBaseRepo(),
            new ThrowingOllamaClient(new HttpRequestException("Ollama returned non-success status 503")),
            opts);

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GenerateAnswerAsync("hello"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_ThrowsIOException_WhenKnowledgeBaseThrows()
    {
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });
        var svc = new ChatService(
            new ThrowingKbRepo(),
            new FakeOllamaClient("answer"),
            opts);

        await Assert.ThrowsAsync<System.IO.IOException>(() => svc.GenerateAnswerAsync("hello"));
    }
}
