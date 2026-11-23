namespace Ratio.Application.Cases;

public static class CaseNumbers
{
    private const int Length = 20;

    public static string? Digits(string input)
    {
        if (input.Any(character => !char.IsAsciiDigit(character) && character is not ('.' or '-')))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == Length ? digits : null;
    }
}
