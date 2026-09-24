using Ratio.Application.Abstractions;

namespace Ratio.Application.Unavailable;

public static class UnavailableBlocks
{
    public static IReadOnlyList<UnavailableBlock> Sourceless { get; } =
    [
        new("caseLawCitation", UnavailableReason.SourceUnavailable,
            "A citação de acórdão depende do inteiro teor da decisão, e os tribunais do escopo bloqueiam a coleta desse texto."),
        new("citedDecisions", UnavailableReason.SourceUnavailable,
            "As decisões citadas dependem do inteiro teor, que os tribunais do escopo não liberam para coleta."),
        new("amountAwarded", UnavailableReason.SourceUnavailable,
            "O valor fixado não é campo estruturado no DataJud; ele só existe no inteiro teor da decisão."),
        new("reporterJudge", UnavailableReason.SourceUnavailable,
            "O DataJud não publica o relator.")
    ];

    public static IReadOnlyList<UnavailableBlock> ForTheme(long judgedCount, bool hasSummary) =>
        hasSummary ? Sourceless : [.. Sourceless, Summary(judgedCount)];

    public static UnavailableBlock Summary(long judgedCount) =>
        judgedCount == 0
            ? new("summary", UnavailableReason.NotApplicable,
                "Este tema não tem decisões julgadas, então não há entendimento para descrever.")
            : new("summary", UnavailableReason.NotLoaded,
                "O texto deste tema ainda não foi gerado; ele sai na próxima carga.");
}
