using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class ChatController : ControllerBase
{
    private readonly Api.Services.IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(Api.Services.IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Post([FromBody] ChatRequest req)
    {
        var messageText = req?.Message;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogWarning("Rejected chat request: missing or empty message");
            return BadRequest(new { error = "message required" });
        }

        var finalAnswer = await _chatService.GenerateAnswerAsync(messageText!);
        return new JsonResult(new { answer = finalAnswer });
    }
}
