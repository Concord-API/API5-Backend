using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes")]
public class ThemesController(IThemeSearchReader themeSearchReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            return Problem(
                detail: "O parâmetro 'limit' deve estar entre 1 e 100.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var themes = await themeSearchReader.SearchThemesAsync(q, limit ?? 20, cancellationToken);

        return Ok(new { query = q, total = themes.Count, themes });
    }
}
