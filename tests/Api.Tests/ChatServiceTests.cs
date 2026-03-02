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

    private class FakeOllamaClient : Api.Application.IOllamaClient
    {
        private readonly string _resp;
        public FakeOllamaClient(string resp) => _resp = resp;
        public Task<string> GenerateAsync(string endpoint, object payload) => Task.FromResult(_resp);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsPlainText_WhenServiceReturnsPlainText()
    {
        var content = "Plain answer from API";
        var kb = new FakeKnowledgeBaseRepo();
        var client = new FakeOllamaClient(content);
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });

        var svc = new ChatService(kb, client, opts);
        var result = await svc.GenerateAnswerAsync("hello");
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonLines_AndReturnsConcatenatedResponses()
    {
        var content = "{ \"response\": \"First\" }\n{ \"response\": \"Second\" }";
        var kb = new FakeKnowledgeBaseRepo();
        var client = new FakeOllamaClient(content);
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });

        var svc = new ChatService(kb, client, opts);
        var result = await svc.GenerateAnswerAsync("question");
        Assert.Equal("FirstSecond", result);
    }
}
