using Api.Models;
using Api.Services;
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
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid email request");
            return BadRequest(ModelState);
        }

        try
        {
            await _emailService.SendEmailAsync(req, ct);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Email service configuration error");
            return StatusCode(500, new { error = "Email service not configured" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            return StatusCode(500, new { error = "Failed to send email" });
        }
    }
}
