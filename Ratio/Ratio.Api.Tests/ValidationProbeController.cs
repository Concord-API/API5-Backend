using Microsoft.AspNetCore.Mvc;

namespace Ratio.Api.Tests;

[ApiController]
[Route("test/validation-probe")]
public class ValidationProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] int limit) => Ok(new { limit });
}
