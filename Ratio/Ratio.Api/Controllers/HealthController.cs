using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(ILastExtractionReader lastExtractionReader) : ControllerBase
{
    [HttpGet]
    public ContentResult Live()
    {
        return Content("Saudável", "text/plain; charset=utf-8");
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var lastExtractionAt = await lastExtractionReader.GetLastExtractionAsync(cancellationToken);

        return lastExtractionAt is null
            ? Problem(
                title: "Não está pronto",
                detail: "O banco não possui dados carregados ou está inacessível.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
            : Ok(new { status = "Pronto", lastExtractionAt });
    }
}
