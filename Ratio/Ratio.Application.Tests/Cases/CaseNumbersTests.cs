using Ratio.Application.Cases;

namespace Ratio.Application.Tests.Cases;

public class CaseNumbersTests
{
    [Theory]
    [InlineData("1000001-23.2024.8.26.0100")]
    [InlineData("10000012320248260100")]
    public void Reads_the_twenty_digits_of_a_case_number(string input)
    {
        Assert.Equal("10000012320248260100", CaseNumbers.Digits(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1000001-23.2024.8.26")]
    [InlineData("100000123202482601001")]
    [InlineData("1000001-23.2024.8.26.010A")]
    [InlineData("1000001 23.2024.8.26.0100")]
    public void Rejects_what_is_not_a_case_number(string input)
    {
        Assert.Null(CaseNumbers.Digits(input));
    }
}
