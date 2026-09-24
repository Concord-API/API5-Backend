using Ratio.Application.Abstractions;
using Ratio.Application.DataSources;

namespace Ratio.Application.Tests.DataSources;

public class ProvenanceCatalogTests
{
    private static readonly DateTimeOffset LoadedAt = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Names_the_datajud_source_and_links_to_its_public_page()
    {
        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance([new LoadedSource("cases", "datajud", LoadedAt, 12418)], "1.0"));

        var source = Assert.Single(provenance.Sources);
        Assert.Equal("DataJud/CNJ", source.Name);
        Assert.Equal("https://www.cnj.jus.br/sistemas/datajud/", source.SourceUrl);
    }

    [Theory]
    [InlineData("doaj", "DOAJ", "https://doaj.org/")]
    [InlineData("scielo", "SciELO", "https://www.scielo.br/")]
    public void Names_the_doctrine_sources(string code, string name, string url)
    {
        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance([new LoadedSource("doctrine", code, LoadedAt, 40)], "1.0"));

        var source = Assert.Single(provenance.Sources);
        Assert.Equal(name, source.Name);
        Assert.Equal(url, source.SourceUrl);
    }

    [Fact]
    public void Keeps_an_unknown_source_by_its_code_without_a_link()
    {
        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance([new LoadedSource("cases", "pangea", LoadedAt, 7)], "1.0"));

        var source = Assert.Single(provenance.Sources);
        Assert.Equal("pangea", source.Name);
        Assert.Null(source.SourceUrl);
    }

    [Fact]
    public void Keeps_the_block_the_loaded_date_the_count_and_the_methodology()
    {
        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance(
                [
                    new LoadedSource("cases", "datajud", LoadedAt, 12418),
                    new LoadedSource("doctrine", "doaj", LoadedAt.AddDays(3), 40)
                ],
                "1.0"));

        Assert.Equal(["cases", "doctrine"], provenance.Sources.Select(source => source.Block));
        Assert.Equal(["datajud", "doaj"], provenance.Sources.Select(source => source.Source));
        Assert.Equal([LoadedAt, LoadedAt.AddDays(3)], provenance.Sources.Select(source => source.ExtractedAt));
        Assert.Equal([12418L, 40L], provenance.Sources.Select(source => source.Count));
        Assert.Equal("1.0", provenance.MethodologyVersion);
    }

    [Theory]
    [InlineData("cases", "datajud", false)]
    [InlineData("cases", null, true)]
    [InlineData("cases", " ", true)]
    [InlineData(null, "datajud", true)]
    public void Drops_a_source_without_a_name_a_block_or_an_extraction_date(string? block, string? code, bool dated)
    {
        var incomplete = new LoadedSource(block, code, dated ? LoadedAt : null, 12418);

        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance([incomplete, new LoadedSource("doctrine", "doaj", LoadedAt, 40)], "1.0"));

        var source = Assert.Single(provenance.Sources);
        Assert.Equal("doctrine", source.Block);
    }

    [Fact]
    public void Tells_whether_a_block_has_provenance()
    {
        var provenance = ProvenanceCatalog.Describe(
            new LoadedProvenance([new LoadedSource("cases", "datajud", LoadedAt, 12418)], "1.0"));

        Assert.True(provenance.Covers("cases"));
        Assert.False(provenance.Covers("doctrine"));
    }

    [Fact]
    public void Describes_nothing_when_nothing_was_loaded()
    {
        var provenance = ProvenanceCatalog.Describe(LoadedProvenance.Empty);

        Assert.Empty(provenance.Sources);
        Assert.Null(provenance.MethodologyVersion);
    }
}
