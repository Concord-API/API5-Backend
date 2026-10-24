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
}
