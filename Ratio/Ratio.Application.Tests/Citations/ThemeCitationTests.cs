using System.Text.Json;
using Ratio.Application.Abstractions;
using Ratio.Application.Citations;

namespace Ratio.Application.Tests.Citations;

public class ThemeCitationTests
{
    private static readonly DateTimeOffset LoadedAt = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private static readonly ProvenanceSource DataJud =
        new("cases", "datajud", "DataJud/CNJ", "https://www.cnj.jus.br/sistemas/datajud/", LoadedAt, 12418);

    private static readonly ProvenanceSource Doaj =
        new("doctrine", "doaj", "DOAJ", "https://doaj.org/", LoadedAt, 40);

    private static readonly Provenance CasesProvenance = new([DataJud], "1.0");

    private static readonly Scope DeclaredScope = new(
        [
            new("TJMG", "Tribunal de Justiça de Minas Gerais", "MG"),
            new("TJRJ", "Tribunal de Justiça do Rio de Janeiro", "RJ"),
            new("TJSP", "Tribunal de Justiça de São Paulo", "SP")
        ],
        "cível",
        "TJMG, TJRJ e TJSP");

    private static JsonElement[] Segments(string json) => JsonSerializer.Deserialize<JsonElement[]>(json)!;

    private static readonly JsonElement[] Lead = Segments(
        """[{"text":"Em "},{"ratio":0.9861,"n":144,"unit":"decisões"},{"text":" julgadas, houve acolhimento da pretensão do autor."}]""");

    private static ThemeDetail Theme(JsonElement[]? lead, long judgedCount = 144) => new(
        412,
        "Inscrição indevida em cadastro de inadimplentes",
        "CONSUMIDOR",
        78,
        "Dominante",
        203,
        judgedCount,
        3,
        2021,
        2026,
        new DateOnly(2026, 8, 30),
        lead is null ? null : new ThemeNarrative(lead, [], "template", "1.0", new DateOnly(2026, 9, 23)));

    [Fact]
    public void Carries_the_number_the_n_the_source_the_date_and_the_scope_in_one_line()
    {
        var citation = ThemeCitation.Compose(Theme(Lead), CasesProvenance, DeclaredScope);

        Assert.Equal(
            "Inscrição indevida em cadastro de inadimplentes — Em 98,6% das 144 decisões julgadas, houve acolhimento da pretensão do autor. "
            + "Fonte: DataJud/CNJ, 12.418 processos, extração de 28.08.2026. "
            + "Escopo: TJMG, TJRJ e TJSP, matéria cível. Metodologia 1.0.",
            citation);
    }

    [Fact]
    public void Dates_the_citation_by_the_load_in_brasilia_time_not_by_the_clock()
    {
        var lateLoad = DataJud with { ExtractedAt = new DateTimeOffset(2026, 8, 29, 1, 30, 0, TimeSpan.Zero) };

        var citation = ThemeCitation.Compose(Theme(Lead), new Provenance([lateLoad], "1.0"), DeclaredScope);

        Assert.Contains("extração de 28.08.2026", citation);
    }

    [Fact]
    public void Writes_a_count_segment_as_a_count_never_as_a_percentage()
    {
        var lead = Segments(
            """[{"text":"Há "},{"count":1,"unit":"decisão julgada"},{"text":" neste tema."}]""");

        var citation = ThemeCitation.Compose(Theme(lead, judgedCount: 1), CasesProvenance, DeclaredScope);

        Assert.Contains("— Há 1 decisão julgada neste tema. Fonte:", citation);
        Assert.DoesNotContain("%", citation);
    }

    [Fact]
    public void Drops_a_ratio_segment_without_judged_cases()
    {
        var lead = Segments(
            """[{"text":"Sem julgados"},{"ratio":0,"n":0,"unit":"decisões"},{"text":"."}]""");

        var citation = ThemeCitation.Compose(Theme(lead, judgedCount: 0), CasesProvenance, DeclaredScope);

        Assert.Contains("— Sem julgados. Fonte:", citation);
        Assert.DoesNotContain("%", citation);
    }

    [Fact]
    public void Cites_the_judged_count_when_the_theme_has_no_narrative()
    {
        var citation = ThemeCitation.Compose(Theme(null, judgedCount: 12418), CasesProvenance, DeclaredScope);

        Assert.Contains("— 12.418 decisões julgadas. Fonte:", citation);
    }

    [Fact]
    public void Uses_the_singular_for_a_single_judged_case_without_narrative()
    {
        var citation = ThemeCitation.Compose(Theme(null, judgedCount: 1), CasesProvenance, DeclaredScope);

        Assert.Contains("— 1 decisão julgada. Fonte:", citation);
    }

    [Fact]
    public void Returns_no_citation_when_the_cases_have_no_provenance()
    {
        Assert.Null(ThemeCitation.Compose(Theme(Lead), Provenance.None, DeclaredScope));
    }

    [Fact]
    public void Returns_no_citation_when_only_the_doctrine_has_provenance()
    {
        Assert.Null(ThemeCitation.Compose(Theme(Lead), new Provenance([Doaj], "1.0"), DeclaredScope));
    }

    [Fact]
    public void Leaves_the_doctrine_source_out_of_the_citation()
    {
        var citation = ThemeCitation.Compose(Theme(Lead), new Provenance([DataJud, Doaj], "1.0"), DeclaredScope);

        Assert.DoesNotContain("DOAJ", citation);
    }

    [Fact]
    public void Lists_every_source_of_the_cases()
    {
        var second = DataJud with { Source = "outra", Name = "Outra fonte", Count = 1 };

        var citation = ThemeCitation.Compose(Theme(Lead), new Provenance([DataJud, second], "1.0"), DeclaredScope);

        Assert.Contains(
            "Fonte: DataJud/CNJ, 12.418 processos, extração de 28.08.2026; Outra fonte, 1 processo, extração de 28.08.2026.",
            citation);
    }

    [Fact]
    public void Omits_the_scope_when_no_court_is_declared()
    {
        var citation = ThemeCitation.Compose(Theme(Lead), CasesProvenance, new Scope([], "cível", null));

        Assert.DoesNotContain("Escopo", citation);
    }

    [Fact]
    public void Omits_the_methodology_when_its_version_is_unknown()
    {
        var citation = ThemeCitation.Compose(Theme(Lead), CasesProvenance with { MethodologyVersion = null }, DeclaredScope);

        Assert.DoesNotContain("Metodologia", citation);
    }

    [Fact]
    public void Collapses_line_breaks_inside_the_narrative()
    {
        var lead = Segments("""[{"text":"Em\r\n"},{"ratio":0.5,"n":10,"unit":"decisões"},{"text":"\nhouve  acolhimento."}]""");

        var citation = ThemeCitation.Compose(Theme(lead), CasesProvenance, DeclaredScope);

        Assert.Contains("— Em 50,0% das 10 decisões houve acolhimento. Fonte:", citation);
        Assert.DoesNotContain('\n', citation!);
        Assert.DoesNotContain('\r', citation!);
    }
}
