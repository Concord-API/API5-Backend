using Ratio.Application.Abstractions;

namespace Ratio.Application.Cases;

public static class CaseView
{
    private const string SealedMessage = "Este processo está em sigilo; o Ratio não expõe nenhum dado dele.";
    private const string MissingMessage = "O site do tribunal não publica estes campos para este processo.";
    private const string NotLoadedMessage =
        "Os dados deste processo ainda não foram coletados do site do tribunal; eles entram na próxima carga.";

    private static readonly Dictionary<string, string> SiteSources = new()
    {
        ["TJSP"] = "TJSP, consulta pública do e-SAJ"
    };

    private static readonly Dictionary<string, string> SourcelessReasons = new()
    {
        ["TJRJ"] = "a consulta pública do TJRJ só pode ser feita com captcha",
        ["TJMG"] = "a consulta pública do TJMG só pode ser feita com captcha",
        ["STJ"] = "a consulta processual do STJ bloqueia acesso automatizado"
    };

    public static CaseDetail From(CaseSiteRecord record)
    {
        if (record.Sealed || record.SiteStatus == "sealed")
        {
            return new(null, null, CaseAvailability.Sealed, SealedMessage, null, [], [], null);
        }

        if (!SiteSources.TryGetValue(record.Court, out var sourceName))
        {
            return new(null, record.Court, CaseAvailability.SourceUnavailable, SourcelessMessage(record.Court), null, [], [], null);
        }

        if (record.SiteStatus is null || record.SourceUrl is null || record.ExtractedAt is null)
        {
            return new(record.CaseNumber, record.Court, CaseAvailability.NotLoaded, NotLoadedMessage, null, [], [], null);
        }

        var source = new CaseSource(sourceName, record.SourceUrl, record.ExtractedAt.Value);

        if (record.SiteStatus != "found" || record.Fields is null)
        {
            return new(
                record.CaseNumber,
                record.Court,
                CaseAvailability.NotFoundOnSite,
                $"A consulta pública do {record.Court} não encontrou este processo.",
                null,
                [],
                [],
                source);
        }

        var missing = MissingFields(record.Fields);

        return new(
            record.CaseNumber,
            record.Court,
            CaseAvailability.Available,
            missing.Count == 0 ? null : MissingMessage,
            record.Fields,
            missing,
            record.Movements,
            source);
    }

    private static string SourcelessMessage(string court) =>
        "Os dados deste processo não estão disponíveis no Ratio: " +
        (SourcelessReasons.TryGetValue(court, out var reason)
            ? reason
            : $"o Ratio não tem fonte pública para os processos do {court}") + ".";

    private static IReadOnlyList<string> MissingFields(CaseFields fields)
    {
        List<string> missing = [];
        Add(missing, "caseClass", fields.CaseClass is null);
        Add(missing, "subjects", fields.Subjects.Count == 0);
        Add(missing, "judgingBody", fields.JudgingBody is null);
        Add(missing, "judge", fields.Judge is null);
        Add(missing, "filedAt", fields.FiledAt is null);
        Add(missing, "decisionDate", fields.DecisionDate is null);
        Add(missing, "decisionResult", fields.DecisionResult is null);
        Add(missing, "situation", fields.Situation is null);
        return missing;
    }

    private static void Add(List<string> missing, string field, bool isMissing)
    {
        if (isMissing)
        {
            missing.Add(field);
        }
    }
}
