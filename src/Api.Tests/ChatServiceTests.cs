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
    private class FakeHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public FakeHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(resp);
        }
    }

    private class TestHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    [Fact]
    public async Task GenerateAnswerAsync_ReturnsPlainText_WhenServiceReturnsPlainText()
    {
        var content = "Plain answer from API";
        var client = new HttpClient(new FakeHandler(content)) { BaseAddress = new System.Uri("http://localhost") };
        var factory = new TestHttpClientFactory(client);
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });

        var svc = new ChatService(factory, opts);
        var result = await svc.GenerateAnswerAsync("hello");
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task GenerateAnswerAsync_ParsesJsonLines_AndReturnsConcatenatedResponses()
    {
        var content = "{ \"response\": \"First\" }\n{ \"response\": \"Second\" }";
        var client = new HttpClient(new FakeHandler(content)) { BaseAddress = new System.Uri("http://localhost") };
        var factory = new TestHttpClientFactory(client);
        var opts = Options.Create(new ChatOptions { ClientName = "test", GenerateEndpoint = "/api/generate" });

        var svc = new ChatService(factory, opts);
        var result = await svc.GenerateAnswerAsync("question");
        Assert.Equal("FirstSecond", result);
    }
}
