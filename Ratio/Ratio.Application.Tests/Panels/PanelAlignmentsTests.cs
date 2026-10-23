using Ratio.Application.Abstractions;
using Ratio.Application.Panels;

namespace Ratio.Application.Tests.Panels;

public class PanelAlignmentsTests
{
    private static readonly PanelRule Rule = new(DivergencePoints: 20, MinJudgedForDivergence: 5);

    private static ThemePanelCounts Counts(params JudgingBodyCount[] bodies) => new(412, bodies, Rule);

    [Fact]
    public void Shows_each_panel_with_its_n_and_percentage()
    {
        var panels = PanelAlignments.Build(Counts(
            new("TJSP", "3ª Câmara de Direito Privado", true, 6, 4),
            new("TJSP", "5ª Câmara de Direito Privado", true, 2, 2)));

        var court = Assert.Single(panels.Courts);
        Assert.Equal("TJSP", court.CourtCode);
        Assert.Equal(14, court.N);
        Assert.Equal(0.5714m, court.Percentage);
        Assert.Equal(
            [
                new PanelAlignment("3ª Câmara de Direito Privado", 10, 0.6m, 2.9m, false),
                new PanelAlignment("5ª Câmara de Direito Privado", 4, 0.5m, -7.1m, false)
            ],
            court.Panels);
    }

    [Fact]
    public void Hides_a_panel_with_a_single_judgment()
    {
        var panels = PanelAlignments.Build(Counts(
            new("TJSP", "3ª Câmara de Direito Privado", true, 3, 2),
            new("TJSP", "8ª Câmara de Direito Privado", true, 1, 0)));

        var court = Assert.Single(panels.Courts);
        Assert.Equal(["3ª Câmara de Direito Privado"], court.Panels.Select(panel => panel.Name));
        Assert.Equal(1, court.OutsidePanels);
    }

    [Fact]
    public void Counts_the_bodies_that_are_not_panels_in_the_court_only()
    {
        var panels = PanelAlignments.Build(Counts(
            new("STJ", "Terceira Turma", true, 4, 4),
            new("STJ", "GABINETE DO MINISTRO MARCO BUZZI", false, 0, 3),
            new("STJ", null, false, 1, 0)));

        var court = Assert.Single(panels.Courts);
        Assert.Equal(12, court.N);
        Assert.Equal(["Terceira Turma"], court.Panels.Select(panel => panel.Name));
        Assert.Equal(4, court.OutsidePanels);
    }

    [Fact]
    public void Flags_a_panel_that_diverges_from_its_court()
    {
        var panels = PanelAlignments.Build(Counts(
            new("TJMG", "1ª Câmara Cível", true, 9, 1),
            new("TJMG", "2ª Câmara Cível", true, 2, 8)));

        var court = Assert.Single(panels.Courts);
        Assert.All(court.Panels, panel => Assert.True(panel.Divergent));
        Assert.Equal([35m, -35m], court.Panels.Select(panel => panel.DistanceInPoints));
    }

    [Fact]
    public void Leaves_out_a_court_without_a_panel_to_show()
    {
        var panels = PanelAlignments.Build(Counts(
            new("TJRJ", "CAPITAL 1 VARA CIVEL", false, 3, 3),
            new("TJSP", "3ª Câmara de Direito Privado", true, 1, 1)));

        Assert.Equal(["TJSP"], panels.Courts.Select(court => court.CourtCode));
    }

    [Fact]
    public void Orders_courts_by_code_and_panels_by_n()
    {
        var panels = PanelAlignments.Build(Counts(
            new("TJSP", "1ª Câmara", true, 1, 1),
            new("TJSP", "2ª Câmara", true, 3, 3),
            new("STJ", "Quarta Turma", true, 2, 0)));

        Assert.Equal(["STJ", "TJSP"], panels.Courts.Select(court => court.CourtCode));
        Assert.Equal(["2ª Câmara", "1ª Câmara"], panels.Courts[1].Panels.Select(panel => panel.Name));
    }

    [Fact]
    public void Declares_the_basis_and_the_rule()
    {
        var panels = PanelAlignments.Build(Counts());

        Assert.Equal(412, panels.ThemeKey);
        Assert.Equal("provimento do recurso (pretensão de quem recorreu)", panels.Basis);
        Assert.Equal(Rule, panels.Rule);
        Assert.Empty(panels.Courts);
    }
}
