using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Application.Tests.Cases;

public class ThemeCasesCsvTests
{
    private const string Header =
        "Processo;Tribunal;Órgão julgador;Classe;Grau;Data do julgamento;Resultado;Fonte;Link;Tipo do link;Sigilo";

    private static readonly SampleCase PublicCase = new(
        "0001234-56.2024.8.26.0100",
        "TJSP",
        "5ª Câmara de Direito Privado",
        "Apelação Cível",
        "Second",
        new DateOnly(2025, 6, 15),
        "Procedente",
        "datajud",
        "https://esaj.tjsp.jus.br/cpopg/open.do",
        "portal");

    private static readonly ProvenanceSource DataJud = new(
        "cases",
        "datajud",
        "DataJud/CNJ",
        "https://www.cnj.jus.br/sistemas/datajud/",
        new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
        12418);

    private static readonly Scope ThreeCourts = new(
        [
            new("TJMG", "Tribunal de Justiça de Minas Gerais", "MG"),
            new("TJRJ", "Tribunal de Justiça do Rio de Janeiro", "RJ"),
            new("TJSP", "Tribunal de Justiça de São Paulo", "SP")
        ],
        "cível",
        "TJMG, TJRJ e TJSP");

    private static readonly ExportHeader Loaded = new([DataJud], ThreeCourts);

    private static string[] HeaderLines(string csv) => csv.Split("\r\n\r\n", 2)[0].Split("\r\n");

    private static string[] TableLines(string csv) => csv.Split("\r\n\r\n", 2)[1].Split("\r\n");

    [Fact]
    public void Writes_the_source_the_extraction_date_and_the_scope_above_the_table()
    {
        Assert.Equal(
            [
                "Fonte;DataJud/CNJ;https://www.cnj.jus.br/sistemas/datajud/",
                "Data de extração;28/08/2026",
                "Escopo;cível · TJMG, TJRJ e TJSP"
            ],
            HeaderLines(ThemeCasesCsv.Write(Loaded, [])));
    }

    [Fact]
    public void Writes_a_source_and_an_extraction_date_for_each_source()
    {
        var other = DataJud with
        {
            Source = "tjsp",
            Name = "TJSP",
            SourceUrl = null,
            ExtractedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)
        };

        var lines = HeaderLines(ThemeCasesCsv.Write(Loaded with { Sources = [DataJud, other] }, []));

        Assert.Equal(
            [
                "Fonte;DataJud/CNJ;https://www.cnj.jus.br/sistemas/datajud/",
                "Data de extração;28/08/2026",
                "Fonte;TJSP;",
                "Data de extração;02/09/2026",
                "Escopo;cível · TJMG, TJRJ e TJSP"
            ],
            lines);
    }

    [Fact]
    public void Leaves_the_source_and_the_extraction_date_empty_without_a_source()
    {
        var lines = HeaderLines(ThemeCasesCsv.Write(Loaded with { Sources = [] }, []));

        Assert.Equal(["Fonte;", "Data de extração;", "Escopo;cível · TJMG, TJRJ e TJSP"], lines);
    }

    [Fact]
    public void Writes_only_the_subject_without_courts_in_scope()
    {
        var lines = HeaderLines(ThemeCasesCsv.Write(Loaded with { Scope = new([], "cível", null) }, []));

        Assert.Equal("Escopo;cível", lines[^1]);
    }

    [Fact]
    public void Writes_only_the_table_header_without_cases()
    {
        Assert.Equal([Header, ""], TableLines(ThemeCasesCsv.Write(Loaded, [])));
    }

    [Fact]
    public void Writes_one_line_per_case_with_the_columns_in_order()
    {
        var lines = TableLines(ThemeCasesCsv.Write(Loaded, [PublicCase]));

        Assert.Equal(
            [
                Header,
                "0001234-56.2024.8.26.0100;TJSP;5ª Câmara de Direito Privado;Apelação Cível;Second;15/06/2025;" +
                "Procedente;datajud;https://esaj.tjsp.jus.br/cpopg/open.do;portal;não",
                ""
            ],
            lines);
    }

    [Fact]
    public void Keeps_the_order_of_the_cases()
    {
        var lines = TableLines(ThemeCasesCsv.Write(
            Loaded,
            [
                PublicCase with { CaseNumber = "CASE-B" },
                PublicCase with { CaseNumber = "CASE-A" }
            ]));

        Assert.StartsWith("CASE-B;", lines[1]);
        Assert.StartsWith("CASE-A;", lines[2]);
    }

    [Fact]
    public void Leaves_a_missing_value_as_an_empty_cell()
    {
        var lines = TableLines(ThemeCasesCsv.Write(Loaded, [PublicCase with { JudgingBody = null, DecisionDate = null }]));

        Assert.Equal(
            "0001234-56.2024.8.26.0100;TJSP;;Apelação Cível;Second;;" +
            "Procedente;datajud;https://esaj.tjsp.jus.br/cpopg/open.do;portal;não",
            lines[1]);
    }

    [Fact]
    public void Writes_a_sealed_case_with_every_cell_empty_but_the_secrecy()
    {
        var lines = TableLines(ThemeCasesCsv.Write(Loaded, [SampleCase.SealedCase]));

        Assert.Equal(";;;;;;;;;;sim", lines[1]);
    }

    [Theory]
    [InlineData("Câmara; Seção", "\"Câmara; Seção\"")]
    [InlineData("Câmara \"A\"", "\"Câmara \"\"A\"\"\"")]
    [InlineData("Câmara\nA", "\"Câmara\nA\"")]
    [InlineData("Câmara, Seção", "Câmara, Seção")]
    public void Quotes_a_cell_with_a_separator_a_quote_or_a_line_break(string judgingBody, string expected)
    {
        var csv = ThemeCasesCsv.Write(Loaded, [PublicCase with { JudgingBody = judgingBody }]);

        Assert.Contains($";TJSP;{expected};Apelação Cível;", csv);
    }
}
