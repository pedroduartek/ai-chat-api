using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Api.Application;
using Api.Options;
using Api.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public class ChatServiceTests
{
    private sealed class FakeKnowledgeBaseRepo : IKnowledgeBaseRepository
    {
        private readonly string _kb;
        public int GetKnowledgeBaseCallCount { get; private set; }
        public string? LastQuery { get; private set; }
        public FakeKnowledgeBaseRepo(string kb = "") => _kb = kb;

        public Task<string> GetKnowledgeBaseAsync()
        {
            GetKnowledgeBaseCallCount++;
            return Task.FromResult(_kb);
        }

        public Task<string> GetRelevantKnowledgeBaseAsync(string query)
        {
            LastQuery = query;
            return Task.FromResult(_kb);
        }
    }

    private sealed class ThrowingKbRepo : IKnowledgeBaseRepository
    {
        public Task<string> GetKnowledgeBaseAsync() => Task.FromException<string>(new System.IO.IOException("KB file not found"));
        public Task<string> GetRelevantKnowledgeBaseAsync(string query) => Task.FromException<string>(new System.IO.IOException("KB file not found"));
    }

    private sealed class FakeChatCompletionClient : IChatCompletionClient
    {
        private readonly string _resp;
        public ChatCompletionRequest? LastRequest { get; private set; }

        public FakeChatCompletionClient(string resp) => _resp = resp;

        public Task<string> GenerateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_resp);
        }

        public async IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield return _resp;
        }
    }

    private sealed class ThrowingChatCompletionClient : IChatCompletionClient
    {
        private readonly System.Exception _ex;
        public ThrowingChatCompletionClient(System.Exception ex) => _ex = ex;
        public Task<string> GenerateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) => Task.FromException<string>(_ex);
        public async IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
        var opts = Microsoft.Extensions.Options.Options.Create(new ChatOptions { ChatEndpoint = "/api/chat" });
        var requestFactory = new ChatRequestFactory(new FakeKnowledgeBaseRepo(kb), opts);
        return new ChatService(requestFactory, new FakeChatCompletionClient(ollamaResponse), new ChatResponseParser(), NullLogger<ChatService>.Instance);
    }

    private static (ChatService svc, FakeChatCompletionClient client, FakeKnowledgeBaseRepo repo) BuildServiceWithClient(string ollamaResponse, string kb = "")
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new ChatOptions { ChatEndpoint = "/api/chat" });
        var repo = new FakeKnowledgeBaseRepo(kb);
        var client = new FakeChatCompletionClient(ollamaResponse);
        var svc = new ChatService(new ChatRequestFactory(repo, opts), client, new ChatResponseParser(), NullLogger<ChatService>.Instance);
        return (svc, client, repo);
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
        var opts = Microsoft.Extensions.Options.Options.Create(new ChatOptions { ChatEndpoint = "/api/chat" });
        var svc = new ChatService(
            new ChatRequestFactory(new FakeKnowledgeBaseRepo(), opts),
            new ThrowingChatCompletionClient(new HttpRequestException("Ollama returned non-success status 503")),
            new ChatResponseParser(),
            NullLogger<ChatService>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GenerateAnswerAsync("hello"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_ThrowsIOException_WhenKnowledgeBaseThrows()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new ChatOptions { ChatEndpoint = "/api/chat" });
        var svc = new ChatService(
            new ChatRequestFactory(new ThrowingKbRepo(), opts),
            new FakeChatCompletionClient("answer"),
            new ChatResponseParser(),
            NullLogger<ChatService>.Instance);

        await Assert.ThrowsAsync<System.IO.IOException>(() => svc.GenerateAnswerAsync("hello"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_UsesFullKnowledgeBaseForPrompt()
    {
        var (svc, client, repo) = BuildServiceWithClient("ok", kb: "FULL KB CONTENT");
        await svc.GenerateAnswerAsync("hi");

        Assert.Equal(1, repo.GetKnowledgeBaseCallCount);
        Assert.Null(repo.LastQuery);
        Assert.Contains("FULL KB CONTENT", client.LastRequest!.Messages[0].Content);
    }

    [Fact]
    public async Task GenerateAnswerAsync_PayloadContainsModelParameters()
    {
        var (svc, client, _) = BuildServiceWithClient("ok");
        await svc.GenerateAnswerAsync("hi");

        Assert.NotNull(client.LastRequest);
        Assert.False(client.LastRequest!.Stream);
        Assert.Equal(0.3, client.LastRequest.Options.Temperature);
        Assert.Equal(0.9, client.LastRequest.Options.TopP);
        Assert.Equal(40, client.LastRequest.Options.TopK);
        Assert.Equal(1.1, client.LastRequest.Options.RepeatPenalty);
        Assert.Equal(256, client.LastRequest.Options.NumPredict);
        Assert.Equal(2048, client.LastRequest.Options.NumCtx);
        Assert.Collection(
            client.LastRequest.Messages,
            message =>
            {
                Assert.Equal("system", message.Role);
                Assert.Contains("You are a friendly assistant", message.Content);
            },
            message =>
            {
                Assert.Equal("user", message.Role);
                Assert.Equal("hi", message.Content);
            });
    }

    [Fact]
    public async Task StreamAnswerAsync_YieldsTokens()
    {
        var (svc, client, _) = BuildServiceWithClient("streamed token");
        var tokens = new List<string>();
        await foreach (var token in svc.StreamAnswerAsync("hello"))
        {
            tokens.Add(token);
        }
        Assert.Single(tokens);
        Assert.Equal("streamed token", tokens[0]);
        Assert.True(client.LastRequest!.Stream);
    }
}
