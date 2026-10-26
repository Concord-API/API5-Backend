using Ratio.Application.Precedents;

namespace Ratio.Application.Tests.Precedents;

public class JudgingBodiesTests
{
    [Theory]
    [InlineData("S1", "Primeira Seção")]
    [InlineData("S2", "Segunda Seção")]
    [InlineData("S3", "Terceira Seção")]
    [InlineData("CE", "Corte Especial")]
    [InlineData(" s2 ", "Segunda Seção")]
    public void Names_the_judging_body_of_the_STJ(string code, string name)
    {
        Assert.Equal(name, JudgingBodies.Name(code));
    }

    [Fact]
    public void Keeps_an_unknown_code_as_it_came()
    {
        Assert.Equal("T3", JudgingBodies.Name("T3"));
    }

    [Fact]
    public void Returns_nothing_without_a_code()
    {
        Assert.Null(JudgingBodies.Name(null));
        Assert.Null(JudgingBodies.Name("  "));
    }
}
