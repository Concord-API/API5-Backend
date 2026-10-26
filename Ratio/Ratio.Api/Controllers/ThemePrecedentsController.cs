using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/precedents")]
public class ThemePrecedentsController(IPrecedentReader precedentReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long key, CancellationToken cancellationToken)
    {
        var precedents = await precedentReader.GetThemeAsync(key, cancellationToken);

        if (precedents is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(precedents);
    }
}
