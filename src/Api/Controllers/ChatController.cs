using System.Collections.Generic;
using Api.Models;
using Api.Security;
using Api.Services.Chat;
using Api.Services.Warmup;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ChatMessageValidator _validator;
    private readonly ILogger<ChatController> _logger;
    private readonly ILastActivityTracker _activityTracker;

    private string GetClientIp()
    {
        try
        {
            if (HttpContext?.Request?.Headers != null && HttpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var cf) && !string.IsNullOrEmpty(cf.ToString()))
                return cf.ToString();

            return HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GetClientIp: failed to determine client IP, returning 'unknown'");
            return "unknown";
        }
    }

    public ChatController(IChatService chatService, ChatMessageValidator validator, ILogger<ChatController> logger, ILastActivityTracker activityTracker)
    {
        _chatService = chatService;
        _validator = validator;
        _logger = logger;
        _activityTracker = activityTracker;
    }

    [EnableRateLimiting(RateLimitPolicyNames.Chat)]
    [RequestSizeLimit(8 * 1024)]
    [HttpPost("chat")]
    public async Task<IActionResult> Post([FromBody] ChatRequest req, CancellationToken ct)
    {
        var validation = ValidateMessage(req?.Message);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = validation.Error });
        }

        var messageText = validation.Message!;
        _activityTracker?.Touch();
        var sw = Stopwatch.StartNew();
        var finalAnswer = await _chatService.GenerateAnswerAsync(messageText, ct);
        var clientIp = GetClientIp();
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ChatMode"] = "sync",
            ["ClientIp"] = clientIp,
            ["Question"] = messageText,
            ["QuestionLength"] = messageText.Length,
            ["Answer"] = finalAnswer,
            ["AnswerLength"] = finalAnswer.Length,
            ["DurationMs"] = sw.ElapsedMilliseconds,
            ["IsFallback"] = string.Equals(finalAnswer, ChatResponseParser.Fallback, StringComparison.Ordinal)
        }))
        {
            _logger.LogInformation("Chat request completed");
        }
        return new JsonResult(new { answer = finalAnswer });
    }

    [EnableRateLimiting(RateLimitPolicyNames.ChatStream)]
    [RequestSizeLimit(8 * 1024)]
    [HttpPost("chat/stream")]
    public async Task Stream([FromBody] ChatRequest req)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var validation = ValidateMessage(req?.Message);
        if (!validation.IsValid)
        {
            await WriteValidationErrorAsync(validation.Error!, cancellationToken);
            return;
        }

        var messageText = validation.Message!;
        _activityTracker?.Touch();
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var sw = Stopwatch.StartNew();
        var tokenCount = 0;
        var characterCount = 0;
        await foreach (var token in _chatService.StreamAnswerAsync(messageText, cancellationToken))
        {
            tokenCount++;
            characterCount += token.Length;
            await Response.WriteAsync($"data: {token}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        var clientIp = GetClientIp();
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ChatMode"] = "stream",
            ["ClientIp"] = clientIp,
            ["Question"] = messageText,
            ["QuestionLength"] = messageText.Length,
            ["DurationMs"] = sw.ElapsedMilliseconds,
            ["TokenCount"] = tokenCount,
            ["StreamedCharacterCount"] = characterCount
        }))
        {
            _logger.LogInformation("Streaming chat request completed");
        }
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private ChatMessageValidationResult ValidateMessage(string? message)
    {
        var validation = _validator.Validate(message);
        if (!validation.IsValid)
        {
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["ClientIp"] = GetClientIp(),
                ["MessageLength"] = message?.Length ?? 0,
                ["Reason"] = validation.Error ?? string.Empty
            }))
            {
                _logger.LogWarning("Chat request rejected");
            }
        }

        return validation;
    }

    private async Task WriteValidationErrorAsync(string error, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status400BadRequest;
        await Response.WriteAsJsonAsync(new { error }, cancellationToken: cancellationToken);
    }
}
