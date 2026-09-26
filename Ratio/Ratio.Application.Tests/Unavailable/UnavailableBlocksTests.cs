using Ratio.Application.Abstractions;
using Ratio.Application.Unavailable;

namespace Ratio.Application.Tests.Unavailable;

public class UnavailableBlocksTests
{
    public static TheoryData<string, string> SourcelessMessages => new()
    {
        { "caseLawCitation", "A citação de acórdão depende do inteiro teor da decisão, e os tribunais do escopo bloqueiam a coleta desse texto." },
        { "citedDecisions", "As decisões citadas dependem do inteiro teor, que os tribunais do escopo não liberam para coleta." },
        { "amountAwarded", "O valor fixado não é campo estruturado no DataJud; ele só existe no inteiro teor da decisão." },
        { "reporterJudge", "O DataJud não publica o relator." }
    };

    [Theory]
    [MemberData(nameof(SourcelessMessages))]
    public void Explains_each_sourceless_block_with_its_own_message(string block, string message)
    {
        var item = Assert.Single(UnavailableBlocks.Sourceless, candidate => candidate.Block == block);

        Assert.Equal(UnavailableReason.SourceUnavailable, item.Reason);
        Assert.Equal(message, item.Message);
    }

    [Fact]
    public void Lists_only_the_blocks_of_the_contract()
    {
        Assert.Equal(
            ["caseLawCitation", "citedDecisions", "amountAwarded", "reporterJudge"],
            UnavailableBlocks.Sourceless.Select(item => item.Block));
    }

    [Fact]
    public void Never_repeats_a_message_across_blocks()
    {
        var messages = UnavailableBlocks.Sourceless.Select(item => item.Message).ToArray();

        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
    }
}

public class SummaryUnavailableTests
{
    [Fact]
    public void Says_the_summary_does_not_apply_to_a_theme_without_judged_cases()
    {
        var item = UnavailableBlocks.Summary(judgedCount: 0);

        Assert.Equal("summary", item.Block);
        Assert.Equal(UnavailableReason.NotApplicable, item.Reason);
        Assert.Equal("Este tema não tem decisões julgadas, então não há entendimento para descrever.", item.Message);
    }

    [Fact]
    public void Says_the_summary_was_not_loaded_yet_for_a_theme_with_judged_cases()
    {
        var item = UnavailableBlocks.Summary(judgedCount: 144);

        Assert.Equal("summary", item.Block);
        Assert.Equal(UnavailableReason.NotLoaded, item.Reason);
        Assert.Equal("O texto deste tema ainda não foi gerado; ele sai na próxima carga.", item.Message);
    }
}

public class RelatedDoctrineUnavailableTests
{
    [Fact]
    public void Says_related_doctrine_does_not_apply_without_links_above_the_threshold()
    {
        var item = UnavailableBlocks.RelatedDoctrine();

        Assert.Equal("relatedDoctrine", item.Block);
        Assert.Equal(UnavailableReason.NotApplicable, item.Reason);
        Assert.Equal("Não há doutrina relacionada a este tema acima do limiar de similaridade declarado.", item.Message);
    }
}
