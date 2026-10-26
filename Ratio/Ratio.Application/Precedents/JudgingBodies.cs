namespace Ratio.Application.Precedents;

public static class JudgingBodies
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S1"] = "Primeira Seção",
        ["S2"] = "Segunda Seção",
        ["S3"] = "Terceira Seção",
        ["CE"] = "Corte Especial"
    };

    public static string? Name(string? code)
    {
        var trimmed = code?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return Names.TryGetValue(trimmed, out var name) ? name : trimmed;
    }
}
