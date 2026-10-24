using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/cases")]
public class ThemeCasesController(IThemeCasesReader themeCasesReader) : ControllerBase
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

        var cases =await themeCasesReader.ListCasesAsync(key, limit ?? 20, cancellationToken);

        if (cases is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(cases);
    }
}
