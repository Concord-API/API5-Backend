using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.Panels;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/panels")]
public class ThemePanelsController(IPanelAlignmentReader panelAlignmentReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long key, CancellationToken cancellationToken)
    {
        var counts = await panelAlignmentReader.GetThemeAsync(key, cancellationToken);

        if (counts is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(PanelAlignments.Build(counts));
    }
}
