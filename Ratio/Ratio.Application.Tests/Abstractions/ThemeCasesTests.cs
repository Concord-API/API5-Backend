using Ratio.Application.Abstractions;

namespace Ratio.Application.Tests.Abstractions;

public class ThemeCasesTests
{
    private static readonly SampleCase Case = new(
        "0001234-56.2024.8.26.0100",
        "TJSP",
        "5ª Câmara de Direito Privado",
        "Apelação Cível",
        "Second",
        new DateOnly(2025, 6, 15),
        "Procedente",
        "datajud",
        null,
        null);

    [Fact]
    public void Describes_the_sample_with_the_thousands_separator()
    {
        var cases = new ThemeCases(412, Enumerable.Repeat(Case, 20).ToArray(), 12418);

        Assert.Equal(20, cases.Shown);
        Assert.Equal("20 de 12.418 decisões", cases.SampleLabel);
    }

    [Fact]
    public void Describes_a_single_decision_in_the_singular()
    {
        var cases = new ThemeCases(412, [Case], 1);

        Assert.Equal("1 de 1 decisão", cases.SampleLabel);
    }

    [Fact]
    public void Describes_a_theme_without_decisions()
    {
        var cases = new ThemeCases(413, [], 0);

        Assert.Equal(0, cases.Shown);
        Assert.Equal("0 de 0 decisões", cases.SampleLabel);
    }
}
