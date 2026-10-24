namespace Ratio.Application.Cases;

public static class CaseColumns
{
    private static readonly Dictionary<string, string> AppealOutcomes = new()
    {
        ["Granted"] = "Recurso provido",
        ["PartiallyGranted"] = "Recurso provido em parte",
        ["Denied"] = "Recurso não provido"
    };

    public static string Outcome(string outcomeCode, string outcomeLabel, string? polarityReference) =>
        polarityReference == "pretensao_recorrente" && AppealOutcomes.TryGetValue(outcomeCode, out var appealOutcome)
            ? appealOutcome
            : outcomeLabel;
}
