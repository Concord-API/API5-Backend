using Ratio.Application.Abstractions;

namespace Ratio.Application.Coverage;

public static class DeclaredScope
{
    private const string Subject = "cível";

    public static Scope Describe(IReadOnlyList<ScopeCourt> courts) =>
        new(courts, Subject, Statement(courts.Select(court => court.Code).ToArray()));

    private static string? Statement(string[] codes) => codes.Length switch
    {
        0 => null,
        1 => codes[0],
        _ => $"{string.Join(", ", codes[..^1])} e {codes[^1]}"
    };
}
