using System.Threading.Tasks;
using Api.Controllers;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Api.Tests;

public class ChatControllerTests
{
    private static ChatController BuildController(Mock<Api.Services.IChatService> svcMock)
    {
        var tracker = new Mock<Api.Services.ILastActivityTracker>();
        return new ChatController(svcMock.Object, NullLogger<ChatController>.Instance, tracker.Object);
    }

    [Fact]
    public async Task Post_ReturnsBadRequest_WhenMessageMissing()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        var controller = BuildController(svcMock);

        var result = await controller.Post(new ChatRequest { Message = null });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Post_ReturnsBadRequest_WhenMessageIsWhitespace()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        var controller = BuildController(svcMock);

        var result = await controller.Post(new ChatRequest { Message = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
        svcMock.Verify(s => s.GenerateAnswerAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Post_ReturnsBadRequest_WhenMessageIsEmptyString()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        var controller = BuildController(svcMock);

        var result = await controller.Post(new ChatRequest { Message = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Post_ReturnsBadRequest_WhenMessageExceedsMaxLength()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        var controller = BuildController(svcMock);
        var longMessage = new string('a', 501);

        var result = await controller.Post(new ChatRequest { Message = longMessage });

        Assert.IsType<BadRequestObjectResult>(result);
        svcMock.Verify(s => s.GenerateAnswerAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Post_ReturnsJsonResult_WithAnswer()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        svcMock.Setup(s => s.GenerateAnswerAsync(It.IsAny<string>())).ReturnsAsync("the-answer");
        var controller = BuildController(svcMock);

        var result = await controller.Post(new ChatRequest { Message = "hello" });

        var jr = Assert.IsType<JsonResult>(result);
        var valType = jr.Value!.GetType();
        var prop = valType.GetProperty("answer");
        Assert.NotNull(prop);
        Assert.Equal("the-answer", prop.GetValue(jr.Value));
    }

    [Fact]
    public async Task Post_AcceptsMessage_AtExactMaxLength()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        svcMock.Setup(s => s.GenerateAnswerAsync(It.IsAny<string>())).ReturnsAsync("ok");
        var controller = BuildController(svcMock);
        var exactMessage = new string('a', 500);

        var result = await controller.Post(new ChatRequest { Message = exactMessage });

        Assert.IsType<JsonResult>(result);
        svcMock.Verify(s => s.GenerateAnswerAsync(exactMessage), Times.Once);
    }

    [Fact]
    public async Task Post_ThrowsException_WhenServiceThrows()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        svcMock.Setup(s => s.GenerateAnswerAsync(It.IsAny<string>()))
               .ThrowsAsync(new System.Net.Http.HttpRequestException("Ollama returned non-success status 503"));
        var controller = BuildController(svcMock);

        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
            () => controller.Post(new ChatRequest { Message = "hello" }));
    }
}
