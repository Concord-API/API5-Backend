using Ratio.Application.Abstractions;

namespace Ratio.Application.Panels;

public sealed record PanelAlignment(string Name, long N, decimal Percentage, decimal DistanceInPoints, bool Divergent);

public sealed record CourtPanels(
    string CourtCode,
    long N,
    decimal? Percentage,
    long OutsidePanels,
    IReadOnlyList<PanelAlignment> Panels);

public sealed record ThemePanels(long ThemeKey, string Basis, PanelRule Rule, IReadOnlyList<CourtPanels> Courts);

public static class PanelAlignments
{
    public const string Basis = "provimento do recurso (pretensão de quem recorreu)";

    private const long MinJudgedToShow = 2;

    public static ThemePanels Build(ThemePanelCounts counts)
    {
        var courts = counts.Bodies
            .GroupBy(body => body.CourtCode)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Court(group.Key, group.ToArray(), counts.Rule))
            .Where(court => court.Panels.Count > 0)
            .ToArray();

        return new ThemePanels(counts.ThemeKey, Basis, counts.Rule, courts);
    }

    private static CourtPanels Court(string courtCode, JudgingBodyCount[] bodies, PanelRule rule)
    {
        var judged = bodies.Sum(Judged);
        decimal? courtRate = judged > 0 ? (decimal)bodies.Sum(body => body.Upheld) / judged : null;

        var panels = bodies
            .Where(body => body.IsPanel && body.BodyName is not null && Judged(body) >= MinJudgedToShow)
            .OrderByDescending(Judged)
            .ThenBy(body => body.BodyName, StringComparer.Ordinal)
            .Select(body => Panel(body, courtRate!.Value, rule))
            .ToArray();

        return new CourtPanels(
            courtCode,
            judged,
            courtRate is null ? null : Math.Round(courtRate.Value, 4),
            judged - panels.Sum(panel => panel.N),
            panels);
    }

    private static PanelAlignment Panel(JudgingBodyCount body, decimal courtRate, PanelRule rule)
    {
        var judged = Judged(body);
        var rate = (decimal)body.Upheld / judged;

        return new PanelAlignment(
            body.BodyName!,
            judged,
            Math.Round(rate, 4),
            PanelDivergence.DistanceInPoints(rate, courtRate),
            PanelDivergence.Diverges(rate, courtRate, judged, rule));
    }

    private static long Judged(JudgingBodyCount body) => body.Upheld + body.Rejected;
}
