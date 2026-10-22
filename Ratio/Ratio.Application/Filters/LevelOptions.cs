using System.Diagnostics.CodeAnalysis;
using Ratio.Application.Abstractions;

namespace Ratio.Application.Filters;

public static class LevelOptions
{
    private static readonly (LevelOption Option, string[] Levels)[] Options =
    [
        (new LevelOption("first", "Primeiro grau"), ["First", "SpecialCourt"]),
        (new LevelOption("second", "Segundo grau"), ["Second", "AppealPanel"]),
        (new LevelOption("superior", "Superior"), ["Superior"])
    ];

    public static IReadOnlyList<LevelOption> Describe(IReadOnlyCollection<string> loadedLevels) =>
        Options
            .Where(entry => entry.Levels.Any(loadedLevels.Contains))
            .Select(entry => entry.Option)
            .ToArray();

    public static bool TryGetLevels(string code, [NotNullWhen(true)] out string[]? levels)
    {
        levels = Options.FirstOrDefault(entry => entry.Option.Code == code).Levels;
        return levels is not null;
    }
}
