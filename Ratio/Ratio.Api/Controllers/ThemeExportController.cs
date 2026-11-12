using System.Text;
using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.Cases;
using Ratio.Application.Coverage;
using Ratio.Application.DataSources;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes/{key:long}/export")]
public class ThemeExportController(
    IThemeCasesReader themeCasesReader,
    IProvenanceReader provenanceReader,
    IScopeReader scopeReader) : ControllerBase
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

        var provenance = ProvenanceCatalog.Describe(await provenanceReader.GetThemeAsync(key, cancellationToken));
        var scope = DeclaredScope.Describe(await scopeReader.GetCourtsAsync(cancellationToken));
        var header = new ExportHeader(provenance.Sources.Where(source => source.Block == "cases").ToArray(), scope);

        byte[] content = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(ThemeCasesCsv.Write(header, cases))];

        return File(content, "text/csv; charset=utf-8", $"tema-{key}.csv");
    }
}
