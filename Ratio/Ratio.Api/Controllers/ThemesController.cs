using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.Coverage;
using Ratio.Application.DataSources;
using Ratio.Application.Unavailable;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/themes")]
public class ThemesController(
    IThemeSearchReader themeSearchReader,
    IThemeDetailReader themeDetailReader,
    IProvenanceReader provenanceReader,
    IDoctrineReader doctrineReader,
    IScopeReader scopeReader,
    ICourtCaseCountReader courtCaseCountReader) : ControllerBase
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
        var summary = provenance.Covers("cases") ? theme.Summary : null;
        var hasDoctrineProvenance = provenance.Covers("doctrine");
        var relatedDoctrine = hasDoctrineProvenance
            ? RelatedDoctrine.Empty with
            {
                Entries = await doctrineReader.GetRelatedAsync(key, RelatedDoctrine.MaxEntries, cancellationToken)
            }
            : RelatedDoctrine.Empty;
        var unavailable = UnavailableBlocks.ForTheme(theme.JudgedCount, summary is not null);

        if (hasDoctrineProvenance && relatedDoctrine.Entries.Count == 0)
        {
            unavailable = [.. unavailable, UnavailableBlocks.RelatedDoctrine()];
        }

        var scope = DeclaredScope.Describe(await scopeReader.GetCourtsAsync(cancellationToken));

        return Ok(theme with
        {
            Summary = summary,
            Unavailable = unavailable,
            Provenance = provenance,
            RelatedDoctrine = relatedDoctrine,
            Scope = scope
        });
    }

    private static readonly Dictionary<string, int?> Periods = new()
    {
        ["5y"] = 5,
        ["2y"] = 2,
        ["all"] = null
    };

    private static readonly Dictionary<string, string[]> Levels = new()
    {
        ["first"] = ["First", "SpecialCourt"],
        ["second"] = ["Second", "AppealPanel"],
        ["superior"] = ["Superior"]
    };

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromQuery] string[]? court,
        [FromQuery] string? period,
        [FromQuery] string? level,
        [FromQuery] int? minStrength,
        CancellationToken cancellationToken)
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

        if (period is not null && !Periods.ContainsKey(period))
        {
            return Problem(
                detail: "Período inválido: use 5y, 2y ou all.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (level is not null && !Levels.ContainsKey(level))
        {
            return Problem(
                detail: "Grau inválido: use first, second ou superior.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (minStrength is < 0 or > 100)
        {
            return Problem(
                detail: "O parâmetro 'minStrength' deve estar entre 0 e 100.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var filters = new ThemeFilters(
            court is { Length: > 0 } ? court : null,
            period is null ? null : Periods[period],
            level is null ? null : Levels[level],
            minStrength);

        var provenance = ProvenanceCatalog.Describe(await provenanceReader.GetGlobalAsync(cancellationToken));

        var scope = DeclaredScope.Describe(await scopeReader.GetCourtsAsync(cancellationToken));

        var filterOptions = new { courts = await courtCaseCountReader.GetAsync(cancellationToken) };

        if (string.IsNullOrWhiteSpace(q))
        {
            var topThemes = await themeSearchReader.TopThemesAsync(limit ?? 20, filters, cancellationToken);

            return Ok(new { query = "", total = topThemes.Count, themes = topThemes, provenance, scope, filterOptions });
        }

        var themes = await themeSearchReader.SearchThemesAsync(q, limit ?? 20, filters, cancellationToken);

        return Ok(new { query = q, total = themes.Count, themes, provenance, scope, filterOptions });
    }
}
