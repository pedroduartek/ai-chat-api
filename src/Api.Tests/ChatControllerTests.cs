using System.Threading.Tasks;
using Api.Controllers;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Api.Tests;

public class ChatControllerTests
{
    [Fact]
    public async Task Post_ReturnsBadRequest_WhenMessageMissing()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        var controller = new ChatController(svcMock.Object);

        var result = await controller.Post(new ChatRequest { Message = null });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Post_ReturnsJsonResult_WithAnswer()
    {
        var svcMock = new Mock<Api.Services.IChatService>();
        svcMock.Setup(s => s.GenerateAnswerAsync(It.IsAny<string>())).ReturnsAsync("the-answer");
        var controller = new ChatController(svcMock.Object);

        var result = await controller.Post(new ChatRequest { Message = "hello" });

        var jr = Assert.IsType<JsonResult>(result);
        var valType = jr.Value!.GetType();
        var prop = valType.GetProperty("answer");
        Assert.NotNull(prop);
        var v = prop.GetValue(jr.Value);
        Assert.Equal("the-answer", v);
    }
}
