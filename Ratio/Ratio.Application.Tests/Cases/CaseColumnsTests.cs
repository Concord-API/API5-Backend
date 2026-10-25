using Ratio.Application.Cases;

namespace Ratio.Application.Tests.Cases;

public class CaseColumnsTests
{
    [Theory]
    [InlineData("Granted", "Procedente", "Recurso provido")]
    [InlineData("PartiallyGranted", "Parcialmente procedente", "Recurso provido em parte")]
    [InlineData("Denied", "Improcedente", "Recurso não provido")]
    public void Describes_an_appeal_outcome_by_whether_the_appeal_was_granted(
        string outcomeCode, string outcomeLabel, string expected)
    {
        Assert.Equal(expected, CaseColumns.Outcome(outcomeCode, outcomeLabel, "pretensao_recorrente"));
    }

    [Theory]
    [InlineData("pretensao_autor")]
    [InlineData(null)]
    public void Keeps_the_outcome_label_of_a_claim(string? polarityReference)
    {
        Assert.Equal("Procedente", CaseColumns.Outcome("Granted", "Procedente", polarityReference));
    }

    [Theory]
    [InlineData("Terceira Turma")]
    [InlineData("Segunda Seção")]
    [InlineData("Corte Especial")]
    public void Keeps_a_panel_a_section_or_the_Special_Court_of_the_STJ(string bodyName)
    {
        Assert.Equal(bodyName, CaseColumns.JudgingBody("STJ", bodyName));
    }

    [Theory]
    [InlineData("GABINETE DO MINISTRO MARCO AURÉLIO BELLIZZE")]
    [InlineData("Presidência")]
    [InlineData(null)]
    public void Leaves_the_chamber_of_the_STJ_empty_when_it_is_not_a_panel(string? bodyName)
    {
        Assert.Null(CaseColumns.JudgingBody("STJ", bodyName));
    }

    [Fact]
    public void Keeps_the_chamber_of_a_state_court()
    {
        Assert.Equal("5ª Câmara de Direito Privado", CaseColumns.JudgingBody("TJSP", "5ª Câmara de Direito Privado"));
    }
}
