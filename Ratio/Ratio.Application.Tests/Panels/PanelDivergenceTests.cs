using Ratio.Application.Panels;

namespace Ratio.Application.Tests.Panels;

public class PanelDivergenceTests
{
    private static readonly PanelRule Rule = new(DivergencePoints: 20, MinJudgedForDivergence: 5);

    [Fact]
    public void Flags_a_panel_exactly_the_threshold_away_from_its_court()
    {
        Assert.True(PanelDivergence.Diverges(panelRate: 0.70m, courtRate: 0.50m, panelJudged: 5, Rule));
    }

    [Fact]
    public void Flags_a_panel_below_its_court_as_well()
    {
        Assert.True(PanelDivergence.Diverges(panelRate: 0.25m, courtRate: 0.60m, panelJudged: 8, Rule));
    }

    [Fact]
    public void Does_not_flag_a_panel_closer_than_the_threshold()
    {
        Assert.False(PanelDivergence.Diverges(panelRate: 0.69m, courtRate: 0.50m, panelJudged: 30, Rule));
    }

    [Fact]
    public void Does_not_flag_a_panel_below_the_minimum_judged()
    {
        Assert.False(PanelDivergence.Diverges(panelRate: 1.00m, courtRate: 0.10m, panelJudged: 4, Rule));
    }

    [Fact]
    public void Reads_the_threshold_and_minimum_from_the_rule()
    {
        var strict = new PanelRule(DivergencePoints: 10, MinJudgedForDivergence: 3);

        Assert.True(PanelDivergence.Diverges(panelRate: 0.60m, courtRate: 0.50m, panelJudged: 3, strict));
    }

    [Fact]
    public void Measures_the_distance_in_percentage_points()
    {
        Assert.Equal(-35, PanelDivergence.DistanceInPoints(panelRate: 0.25m, courtRate: 0.60m));
    }
}
