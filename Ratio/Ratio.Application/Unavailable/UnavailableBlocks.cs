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
}
