using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Application.Tests.Cases;

public class ThemeCasesCsvTests
{
    private const string Header =
        "Processo,Tribunal,Órgão julgador,Classe,Grau,Data do julgamento,Resultado,Fonte,Link,Tipo do link,Sigilo";

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

    private static string[] Lines(string csv) => csv.Split("\r\n");

    [Fact]
    public void Writes_only_the_header_without_cases()
    {
        Assert.Equal(Header + "\r\n", ThemeCasesCsv.Write([]));
    }

    [Fact]
    public void Writes_one_line_per_case_with_the_columns_in_order()
    {
        var lines = Lines(ThemeCasesCsv.Write([PublicCase]));

        Assert.Equal(
            [
                Header,
                "0001234-56.2024.8.26.0100,TJSP,5ª Câmara de Direito Privado,Apelação Cível,Second,2025-06-15," +
                "Procedente,datajud,https://esaj.tjsp.jus.br/cpopg/open.do,portal,não",
                ""
            ],
            lines);
    }

    [Fact]
    public void Keeps_the_order_of_the_cases()
    {
        var lines = Lines(ThemeCasesCsv.Write(
        [
            PublicCase with { CaseNumber = "CASE-B" },
            PublicCase with { CaseNumber = "CASE-A" }
        ]));

        Assert.StartsWith("CASE-B,", lines[1]);
        Assert.StartsWith("CASE-A,", lines[2]);
    }

    [Fact]
    public void Leaves_a_missing_value_as_an_empty_cell()
    {
        var lines = Lines(ThemeCasesCsv.Write([PublicCase with { JudgingBody = null, DecisionDate = null }]));

        Assert.Equal(
            "0001234-56.2024.8.26.0100,TJSP,,Apelação Cível,Second,," +
            "Procedente,datajud,https://esaj.tjsp.jus.br/cpopg/open.do,portal,não",
            lines[1]);
    }

    [Fact]
    public void Writes_a_sealed_case_with_every_cell_empty_but_the_secrecy()
    {
        var lines = Lines(ThemeCasesCsv.Write([SampleCase.SealedCase]));

        Assert.Equal(",,,,,,,,,,sim", lines[1]);
    }

    [Theory]
    [InlineData("Câmara, Seção", "\"Câmara, Seção\"")]
    [InlineData("Câmara \"A\"", "\"Câmara \"\"A\"\"\"")]
    [InlineData("Câmara\nA", "\"Câmara\nA\"")]
    public void Quotes_a_cell_with_a_separator_a_quote_or_a_line_break(string judgingBody, string expected)
    {
        var csv = ThemeCasesCsv.Write([PublicCase with { JudgingBody = judgingBody }]);

        Assert.Contains($",TJSP,{expected},Apelação Cível,", csv);
    }
}
