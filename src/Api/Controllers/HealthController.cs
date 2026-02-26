using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Get() => Ok(new { status = "ok" });
}
