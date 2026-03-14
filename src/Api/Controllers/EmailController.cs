using System.Collections.Generic;
using Api.Models;
using Api.Services.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class EmailController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailController> _logger;

    public EmailController(IEmailService emailService, ILogger<EmailController> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("email")]
    public async Task<IActionResult> Post([FromBody] EmailRequest req, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SubjectLength"] = req.Subject?.Length ?? 0,
            ["BodyLength"] = req.Body?.Length ?? 0,
            ["IsHtml"] = req.IsHtml
        });

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Email request validation failed");
            return BadRequest(ModelState);
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
}
