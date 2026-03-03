using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class ChatController : ControllerBase
{
    private readonly Api.Services.IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    private const int MaxMessageLength = 500;

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

        if (messageText.Length > MaxMessageLength)
        {
            _logger.LogWarning("Rejected chat request: message too long ({Length} chars)", messageText.Length);
            return BadRequest(new { error = $"message must be {MaxMessageLength} characters or fewer" });
        }

        if (InputSanitizer.ContainsInjection(messageText))
        {
            _logger.LogWarning("Rejected chat request: prompt injection detected");
            return BadRequest(new { error = "Your message contains disallowed content." });
        }

        var finalAnswer = await _chatService.GenerateAnswerAsync(messageText!);
        return new JsonResult(new { answer = finalAnswer });
    }

    [HttpPost("chat/stream")]
    public async Task Stream([FromBody] ChatRequest req)
    {
        var messageText = req?.Message;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("{\"error\":\"message required\"}");
            return;
        }

        if (messageText.Length > MaxMessageLength)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync($"{{\"error\":\"message must be {MaxMessageLength} characters or fewer\"}}");
            return;
        }

        if (InputSanitizer.ContainsInjection(messageText))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("{\"error\":\"Your message contains disallowed content.\"}");
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await foreach (var token in _chatService.StreamAnswerAsync(messageText!, HttpContext.RequestAborted))
        {
            await Response.WriteAsync($"data: {token}\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }

        await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
        await Response.Body.FlushAsync(HttpContext.RequestAborted);
    }
}
