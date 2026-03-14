using System.Collections.Generic;
using Api.Models;
using Api.Security;
using Api.Services.Email;
using Api.Services.Turnstile;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class EmailController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly ITurnstileVerificationService _turnstileVerificationService;
    private readonly ILogger<EmailController> _logger;

    public EmailController(
        IEmailService emailService,
        ITurnstileVerificationService turnstileVerificationService,
        ILogger<EmailController> logger)
    {
        _emailService = emailService;
        _turnstileVerificationService = turnstileVerificationService;
        _logger = logger;
    }

    [EnableRateLimiting(RateLimitPolicyNames.Email)]
    [RequestSizeLimit(16 * 1024)]
    [HttpPost("email")]
    public async Task<IActionResult> Post([FromBody] EmailRequest req, CancellationToken ct)
    {
        if (req is null)
        {
            _logger.LogWarning("Email request missing body");
            return BadRequest(new { error = "request body required" });
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SenderNameLength"] = req.Name?.Length ?? 0,
            ["ReplyToLength"] = req.Email?.Length ?? 0,
            ["SubjectLength"] = req.Subject?.Length ?? 0,
            ["BodyLength"] = req.Message?.Length ?? 0,
            ["Source"] = req.Source ?? string.Empty
        });

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Email request validation failed");
            return BadRequest(ModelState);
        }

        var turnstileResult = await _turnstileVerificationService.VerifyAsync(
            req.TurnstileToken,
            GetClientIp(),
            GetExpectedTurnstileAction(req.Source),
            ct);

        if (!turnstileResult.Success)
        {
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["ClientIp"] = GetClientIp(),
                ["FailureReason"] = turnstileResult.FailureReason ?? string.Empty,
                ["TurnstileErrorCodes"] = string.Join(", ", turnstileResult.ErrorCodes ?? [])
            }))
            {
                _logger.LogWarning("Turnstile verification failed for email request");
            }

            return turnstileResult.IsServiceError
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = turnstileResult.UserMessage })
                : BadRequest(new { error = turnstileResult.UserMessage });
        }

        try
        {
            await _emailService.SendEmailAsync(req, ct);
            _logger.LogInformation("Email request accepted");
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Email request failed due to configuration");
            return StatusCode(500, new { error = "Email service not configured" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email request failed");
            return StatusCode(500, new { error = "Failed to send email" });
        }
    }

    private string GetClientIp()
    {
        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var cf) &&
            !string.IsNullOrWhiteSpace(cf.ToString()))
        {
            return cf.ToString();
        }

        return HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string GetExpectedTurnstileAction(string? source) =>
        string.Equals(source, "terminal", StringComparison.OrdinalIgnoreCase)
            ? "terminal_email"
            : "contact_form";
}
