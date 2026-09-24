using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.DataSources;
using Ratio.Application.Unavailable;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes")]
public class ThemesController(
    IThemeSearchReader themeSearchReader,
    IThemeDetailReader themeDetailReader,
    IProvenanceReader provenanceReader) : ControllerBase
{
    [HttpGet("{key:long}")]
    public async Task<IActionResult> GetByKey(long key, CancellationToken cancellationToken)
    {
        var theme = await themeDetailReader.GetThemeAsync(key, cancellationToken);

        if (theme is null)
        {
            return Problem(detail: "Tema não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        var provenance = ProvenanceCatalog.Describe(await provenanceReader.GetThemeAsync(key, cancellationToken));

        return Ok(theme with
        {
            Unavailable = UnavailableBlocks.ForTheme(theme.JudgedCount, theme.Summary is not null),
            Provenance = provenance
        });
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            return Problem(
                detail: "O parâmetro 'limit' deve estar entre 1 e 100.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (q?.Trim().Length is > 0 and < 3)
        {
            return Problem(
                detail: "Digite ao menos 3 caracteres para buscar.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var provenance = ProvenanceCatalog.Describe(await provenanceReader.GetGlobalAsync(cancellationToken));

        if (string.IsNullOrWhiteSpace(q))
        {
            var topThemes = await themeSearchReader.TopThemesAsync(limit ?? 20, cancellationToken);

            return Ok(new { query = "", total = topThemes.Count, themes = topThemes, provenance });
        }

        var themes = await themeSearchReader.SearchThemesAsync(q, limit ?? 20, cancellationToken);

        return Ok(new { query = q, total = themes.Count, themes, provenance });
    }
}
