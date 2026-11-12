using System.Text;
using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/export")]
public class ThemeExportController(IThemeCasesReader themeCasesReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long key, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (format is not null && format != "csv")
        {
            return Problem(detail: "Formato inválido: use csv.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cases = await themeCasesReader.ListAllCasesAsync(key, cancellationToken);

        if (cases is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        return File(Encoding.UTF8.GetBytes(ThemeCasesCsv.Write(cases)), "text/csv", $"tema-{key}.csv");
    }
}
