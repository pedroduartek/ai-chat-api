using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class ChatController : ControllerBase
{
    private readonly Api.Services.IChatService _chatService;

    public ChatController(Api.Services.IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Post([FromBody] ChatRequest req)
    {
        var messageText = req?.Message;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return BadRequest(new { error = "message required" });
        }

        var finalAnswer = await _chatService.GenerateAnswerAsync(messageText!);
        return new JsonResult(new { answer = finalAnswer });
    }
}
