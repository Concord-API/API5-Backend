namespace Ratio.Application.Panels;

public sealed record PanelRule(int DivergencePoints, int MinJudgedForDivergence);

public static class PanelDivergence
{
    public static decimal DistanceInPoints(decimal panelRate, decimal courtRate) =>
        Math.Round((panelRate - courtRate) * 100, 1);

    public static bool Diverges(decimal panelRate, decimal courtRate, long panelJudged, PanelRule rule) =>
        panelJudged >= rule.MinJudgedForDivergence
        && Math.Abs(DistanceInPoints(panelRate, courtRate)) >= rule.DivergencePoints;
}
