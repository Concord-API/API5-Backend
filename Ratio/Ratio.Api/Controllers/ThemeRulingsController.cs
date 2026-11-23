using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/rulings")]
public class ThemeRulingsController(IRulingReader rulingReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long key, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            return Problem(
                detail: "O parâmetro 'limit' deve estar entre 1 e 100.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rulings = await rulingReader.ListAsync(key, limit ?? 20, cancellationToken);

        if (rulings is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(rulings);
    }
}
