using Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    [EnableRateLimiting(RateLimitPolicyNames.Health)]
    [HttpGet("health")]
    public IActionResult Get() => Ok(new { status = "ok" });
}
