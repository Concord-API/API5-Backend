using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemesControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonElement[] Lead = JsonSerializer.Deserialize<JsonElement[]>(
        """[{"text":"Em "},{"ratio":0.9861,"n":144,"unit":"decisões"},{"text":" julgadas, houve acolhimento da pretensão do autor."}]""")!;

    private static readonly JsonElement[] Body = JsonSerializer.Deserialize<JsonElement[]>(
        """[{"text":"As decisões vêm de 3 tribunais, entre 2021 e 2026."}]""")!;

    private static readonly ThemeSummary WrongfulListing = new(
        412,
        "Inscrição indevida em cadastro de inadimplentes",
        "CONSUMIDOR",
        144,
        78,
        "Dominante",
        new ThemeOutcome(142, 2, 0.9861m, "acolhimento da pretensão do autor"),
        2021,
        2026,
        new DateOnly(2026, 8, 30));

    private static readonly ThemeSummary MultiCourtListing = WrongfulListing with
    {
        Courts = ["TJMG", "TJSP", "TJRJ"],
        AdditionalCourtCount = 0
    };

    private static readonly ThemeDetail WrongfulListingDetail = new(
        412,
        "Inscrição indevida em cadastro de inadimplentes",
        "CONSUMIDOR",
        78,
        "Dominante",
        203,
        144,
        3,
        2021,
        2026,
        new DateOnly(2026, 8, 30),
        new ThemeNarrative(Lead, Body, "template", "1.0", new DateOnly(2026, 9, 23)));

    private static readonly LoadedProvenance LoadedCases = ApiFactory.LoadedCases;

    private static void AssertDescribesLoadedCases(JsonElement provenance)
    {
        var source = Assert.Single(provenance.GetProperty("sources").EnumerateArray());
        Assert.Equal("cases", source.GetProperty("block").GetString());
        Assert.Equal("datajud", source.GetProperty("source").GetString());
        Assert.Equal("DataJud/CNJ", source.GetProperty("name").GetString());
        Assert.Equal("https://www.cnj.jus.br/sistemas/datajud/", source.GetProperty("sourceUrl").GetString());
        Assert.Equal("2026-08-28T10:00:00+00:00", source.GetProperty("extractedAt").GetString());
        Assert.Equal(12418, source.GetProperty("count").GetInt64());
        Assert.Equal("1.0", provenance.GetProperty("methodologyVersion").GetString());
    }

    private static void AssertDeclaresTheScope(JsonElement scope)
    {
        Assert.Equal("TJMG, TJRJ e TJSP", scope.GetProperty("statement").GetString());
        Assert.Equal("cível", scope.GetProperty("subject").GetString());
        var court = scope.GetProperty("courts")[0];
        Assert.Equal("TJMG", court.GetProperty("code").GetString());
        Assert.Equal("Tribunal de Justiça de Minas Gerais", court.GetProperty("name").GetString());
        Assert.Equal("MG", court.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Returns_the_scope_with_the_theme()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(430, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 430 });

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/430");

        AssertDeclaresTheScope(body.GetProperty("scope"));
    }

    [Fact]
    public async Task Returns_the_scope_with_the_search()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("negativacao", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=negativacao");

        AssertDeclaresTheScope(body.GetProperty("scope"));
    }

    [Fact]
    public async Task Returns_the_scope_with_the_top_themes()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(11, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?limit=11");

        AssertDeclaresTheScope(body.GetProperty("scope"));
    }

    [Fact]
    public async Task Returns_only_the_narrative_lead_as_the_summary_of_each_result()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("seguro prestamista", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing with { Summary = new ThemeResultSummary(Lead) }]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=seguro%20prestamista");

        var summary = body.GetProperty("themes")[0].GetProperty("summary");
        var lead = summary.GetProperty("lead");
        Assert.Equal(3, lead.GetArrayLength());
        Assert.Equal("Em ", lead[0].GetProperty("text").GetString());
        Assert.Equal(0.9861m, lead[1].GetProperty("ratio").GetDecimal());
        Assert.Equal(144, lead[1].GetProperty("n").GetInt64());
        Assert.Equal("decisões", lead[1].GetProperty("unit").GetString());
        Assert.False(summary.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Returns_a_null_summary_for_a_result_without_narrative()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("tarifa bancaria", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=tarifa%20bancaria");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("themes")[0].GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task Returns_the_provenance_of_the_theme()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(417, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 417 });
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(417, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCases);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/417");

        AssertDescribesLoadedCases(body.GetProperty("provenance"));
    }

    [Fact]
    public async Task Returns_the_court_acronyms_and_the_exact_additional_count()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("cadastro", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MultiCourtListing]);
        factory.ProvenanceReader
            .Setup(reader => reader.GetGlobalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCases);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=cadastro");
        var theme = body.GetProperty("themes")[0];

        Assert.Equal(["TJMG", "TJSP", "TJRJ"], theme.GetProperty("courts").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(0, theme.GetProperty("additionalCourtCount").GetInt32());
    }

    [Fact]
    public async Task Omits_the_summary_when_the_cases_have_no_provenance()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(418, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 418 });
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(418, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedProvenance.Empty);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/418");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("summary").ValueKind);
        Assert.Empty(body.GetProperty("provenance").GetProperty("sources").EnumerateArray());
        var summary = Assert.Single(
            body.GetProperty("unavailable").EnumerateArray(),
            item => item.GetProperty("block").GetString() == "summary");
        Assert.Equal("notLoaded", summary.GetProperty("reason").GetString());
    }

    private static readonly LoadedProvenance LoadedCasesAndDoctrine = new(
        [
            .. LoadedCases.Sources,
            new LoadedSource("doctrine", "oai_emerj", new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), 1)
        ],
        "1.0");

    private static readonly DoctrineEntry HistoricalDamages = new(
        "Dano moral: aspectos históricos e de quantificação",
        "Maria Silva; João Souza",
        "Revista da EMERJ",
        2021,
        "10.1234/emerj.2021.001",
        "https://doi.org/10.1234/emerj.2021.001",
        "oai_emerj",
        0.854,
        "embedding_cosine+lexical",
        "paraphrase-multilingual-MiniLM-L12-v2");

    [Fact]
    public async Task Returns_the_related_doctrine_with_its_declared_threshold()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(430, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 430 });
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(430, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCasesAndDoctrine);
        factory.DoctrineReader
            .Setup(reader => reader.GetRelatedAsync(430, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([HistoricalDamages]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/430");

        var doctrine = body.GetProperty("relatedDoctrine");
        Assert.Equal(0.55, doctrine.GetProperty("threshold").GetDouble());
        var entry = Assert.Single(doctrine.GetProperty("entries").EnumerateArray());
        Assert.Equal("Dano moral: aspectos históricos e de quantificação", entry.GetProperty("title").GetString());
        Assert.Equal("Maria Silva; João Souza", entry.GetProperty("authors").GetString());
        Assert.Equal("Revista da EMERJ", entry.GetProperty("journal").GetString());
        Assert.Equal(2021, entry.GetProperty("publicationYear").GetInt32());
        Assert.Equal("10.1234/emerj.2021.001", entry.GetProperty("doi").GetString());
        Assert.Equal("https://doi.org/10.1234/emerj.2021.001", entry.GetProperty("link").GetString());
        Assert.Equal("oai_emerj", entry.GetProperty("source").GetString());
        Assert.Equal(0.854, entry.GetProperty("similarity").GetDouble());
        Assert.Equal("embedding_cosine+lexical", entry.GetProperty("linkMethod").GetString());
        Assert.Equal("paraphrase-multilingual-MiniLM-L12-v2", entry.GetProperty("embeddingModel").GetString());
    }

    [Fact]
    public async Task Returns_no_related_doctrine_when_the_doctrine_has_no_provenance()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(431, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 431 });
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(431, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCases);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/431");

        var doctrine = body.GetProperty("relatedDoctrine");
        Assert.Equal(0.55, doctrine.GetProperty("threshold").GetDouble());
        Assert.Empty(doctrine.GetProperty("entries").EnumerateArray());
        factory.DoctrineReader.Verify(
            reader => reader.GetRelatedAsync(431, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Explains_when_the_theme_has_no_related_doctrine_above_the_threshold()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(432, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 432 });
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(432, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCasesAndDoctrine);
        factory.DoctrineReader
            .Setup(reader => reader.GetRelatedAsync(432, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/432");

        Assert.Empty(body.GetProperty("relatedDoctrine").GetProperty("entries").EnumerateArray());
        var unavailable = Assert.Single(
            body.GetProperty("unavailable").EnumerateArray(),
            item => item.GetProperty("block").GetString() == "relatedDoctrine");
        Assert.Equal("notApplicable", unavailable.GetProperty("reason").GetString());
        Assert.Equal(
            "Não há doutrina relacionada a este tema acima do limiar de similaridade declarado.",
            unavailable.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Returns_the_global_provenance_with_the_search()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("cadastro", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);
        factory.ProvenanceReader
            .Setup(reader => reader.GetGlobalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCases);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=cadastro");

        AssertDescribesLoadedCases(body.GetProperty("provenance"));
    }

    [Fact]
    public async Task Returns_the_global_provenance_with_the_top_themes()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);
        factory.ProvenanceReader
            .Setup(reader => reader.GetGlobalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoadedCases);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes");

        AssertDescribesLoadedCases(body.GetProperty("provenance"));
    }

    [Fact]
    public async Task Returns_the_theme_header_and_narrative_by_public_key()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(412, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail);

        var response = await _client.GetAsync("/api/themes/412");
        var rawBody = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(rawBody).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("theme_sk", rawBody);
        Assert.DoesNotContain("themeSk", rawBody);
        Assert.Equal(412, body.GetProperty("themeKey").GetInt64());
        Assert.Equal("Inscrição indevida em cadastro de inadimplentes", body.GetProperty("name").GetString());
        Assert.Equal("CONSUMIDOR", body.GetProperty("subjectArea").GetString());
        Assert.Equal(78, body.GetProperty("strengthScore").GetInt32());
        Assert.Equal("Dominante", body.GetProperty("level").GetString());
        Assert.Equal(203, body.GetProperty("caseCount").GetInt64());
        Assert.Equal(144, body.GetProperty("judgedCount").GetInt64());
        Assert.Equal(3, body.GetProperty("courtCount").GetInt64());
        Assert.Equal(2021, body.GetProperty("periodStartYear").GetInt32());
        Assert.Equal(2026, body.GetProperty("periodEndYear").GetInt32());
        Assert.Equal("2026-08-30", body.GetProperty("lastDecisionDate").GetString());

        var summary = body.GetProperty("summary");
        Assert.Equal("Em ", summary.GetProperty("lead")[0].GetProperty("text").GetString());
        Assert.Equal(0.9861m, summary.GetProperty("lead")[1].GetProperty("ratio").GetDecimal());
        Assert.Equal(144, summary.GetProperty("lead")[1].GetProperty("n").GetInt64());
        Assert.Equal("decisões", summary.GetProperty("lead")[1].GetProperty("unit").GetString());
        Assert.Equal("As decisões vêm de 3 tribunais, entre 2021 e 2026.", summary.GetProperty("body")[0].GetProperty("text").GetString());
        Assert.Equal("template", summary.GetProperty("textOrigin").GetString());
        Assert.Equal("1.0", summary.GetProperty("methodologyVersion").GetString());
        Assert.Equal("2026-09-23", summary.GetProperty("generatedAt").GetString());
        factory.ThemeDetailReader.Verify(
            reader => reader.GetThemeAsync(412, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returns_a_null_summary_when_the_theme_has_no_narrative()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(413, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 413, Summary = null });

        var response = await _client.GetAsync("/api/themes/413");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task Returns_the_outcome_breakdown_by_family_with_the_partial_treatment()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(420, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with
            {
                ThemeKey = 420,
                OutcomeBreakdown =
                [
                    new OutcomeBreakdown("acolhimento da pretensão do autor", 144,
                    [
                        new OutcomeCategory("Procedente", 100, 0.6944m),
                        new OutcomeCategory("Parcialmente procedente", 42, 0.2917m),
                        new OutcomeCategory("Improcedente", 2, 0.0139m)
                    ]),
                    new OutcomeBreakdown("acolhimento da pretensão de quem recorreu", 1,
                    [
                        new OutcomeCategory("Procedente", 1, null),
                        new OutcomeCategory("Parcialmente procedente", 0, null),
                        new OutcomeCategory("Improcedente", 0, null)
                    ])
                ]
            });

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/420");

        var breakdown = body.GetProperty("outcomeBreakdown");
        Assert.Equal(2, breakdown.GetArrayLength());

        var merit = breakdown[0];
        Assert.Equal("acolhimento da pretensão do autor", merit.GetProperty("polarityLabel").GetString());
        Assert.Equal(144, merit.GetProperty("judged").GetInt64());
        Assert.Equal(
            ["Procedente", "Parcialmente procedente", "Improcedente"],
            merit.GetProperty("categories").EnumerateArray().Select(category => category.GetProperty("outcome").GetString()));
        Assert.Equal(
            [100L, 42L, 2L],
            merit.GetProperty("categories").EnumerateArray().Select(category => category.GetProperty("count").GetInt64()));
        Assert.Equal(
            [0.6944m, 0.2917m, 0.0139m],
            merit.GetProperty("categories").EnumerateArray().Select(category => category.GetProperty("ratio").GetDecimal()));

        var appeal = breakdown[1];
        Assert.Equal("acolhimento da pretensão de quem recorreu", appeal.GetProperty("polarityLabel").GetString());
        Assert.Equal(1, appeal.GetProperty("judged").GetInt64());
        Assert.All(
            appeal.GetProperty("categories").EnumerateArray(),
            category => Assert.Equal(JsonValueKind.Null, category.GetProperty("ratio").ValueKind));

        Assert.Equal(
            "Na nota de força, a procedência em parte conta como acolhimento.",
            body.GetProperty("partialTreatment").GetString());
    }

    [Fact]
    public async Task Returns_the_alignment_for_each_court_with_n_and_polarity()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(422, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with
            {
                ThemeKey = 422,
                CourtAlignments =
                [
                    new CourtAlignment("TJSP", 10, 0.7m, "acolhimento da pretensão do autor")
                    {
                        Decisions = 12,
                        LastDecisionDate = new DateOnly(2026, 8, 30)
                    },
                    new CourtAlignment("STJ", 4, 0.5m, "provimento do recurso (pretensão do recorrente)")
                ]
            });

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/422");

        var alignments = body.GetProperty("courtAlignments");
        Assert.Equal(2, alignments.GetArrayLength());
        Assert.Equal("TJSP", alignments[0].GetProperty("courtCode").GetString());
        Assert.Equal(10, alignments[0].GetProperty("n").GetInt64());
        Assert.Equal(0.7m, alignments[0].GetProperty("percentage").GetDecimal());
        Assert.Equal("acolhimento da pretensão do autor", alignments[0].GetProperty("polarityLabel").GetString());
        Assert.Equal(12, alignments[0].GetProperty("decisions").GetInt64());
        Assert.Equal("2026-08-30", alignments[0].GetProperty("lastDecisionDate").GetString());
        Assert.Equal(JsonValueKind.Null, alignments[1].GetProperty("lastDecisionDate").ValueKind);
        Assert.Equal("provimento do recurso (pretensão do recorrente)", alignments[1].GetProperty("polarityLabel").GetString());
    }

    [Fact]
    public async Task Returns_an_empty_outcome_breakdown_for_a_theme_without_judged_cases()
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(421, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrongfulListingDetail with { ThemeKey = 421, JudgedCount = 0, Summary = null });

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/421");

        Assert.Equal(0, body.GetProperty("outcomeBreakdown").GetArrayLength());
    }

    private async Task<JsonElement[]> UnavailableOfAsync(ThemeDetail detail)
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(detail.ThemeKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/themes/{detail.ThemeKey}");
        return body.GetProperty("unavailable").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Returns_the_sourceless_blocks_as_unavailable()
    {
        var unavailable = await UnavailableOfAsync(WrongfulListingDetail with { ThemeKey = 414 });

        Assert.Equal(
            ["caseLawCitation", "citedDecisions", "amountAwarded", "reporterJudge"],
            unavailable.Select(item => item.GetProperty("block").GetString()));
        Assert.All(unavailable, item => Assert.Equal("sourceUnavailable", item.GetProperty("reason").GetString()));
        Assert.All(unavailable, item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("message").GetString())));
    }

    [Fact]
    public async Task Explains_a_summary_not_loaded_yet_for_a_judged_theme()
    {
        var unavailable = await UnavailableOfAsync(WrongfulListingDetail with { ThemeKey = 415, Summary = null });

        var summary = Assert.Single(unavailable, item => item.GetProperty("block").GetString() == "summary");
        Assert.Equal("notLoaded", summary.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Explains_that_a_summary_does_not_apply_to_a_theme_without_judged_cases()
    {
        var unavailable = await UnavailableOfAsync(
            WrongfulListingDetail with { ThemeKey = 416, JudgedCount = 0, Summary = null });

        var summary = Assert.Single(unavailable, item => item.GetProperty("block").GetString() == "summary");
        Assert.Equal("notApplicable", summary.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(999999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Returns_not_found_for_an_unknown_theme_key(long key)
    {
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThemeDetail?)null);

        var response = await _client.GetAsync($"/api/themes/{key}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Recurso não encontrado", body.GetProperty("title").GetString());
        Assert.Equal("Tema não encontrado.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Returns_not_found_without_detail_for_a_non_numeric_key()
    {
        var response = await _client.GetAsync("/api/themes/abc");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Recurso não encontrado", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Returns_the_contract_shape_with_theme_key_never_theme_sk()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao indevida", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync("/api/themes?q=inscricao+indevida");
        var rawBody = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(rawBody).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("theme_sk", rawBody);
        Assert.DoesNotContain("caseNumber", rawBody);

        var theme = body.GetProperty("themes")[0];
        Assert.Equal(412, theme.GetProperty("themeKey").GetInt64());
        Assert.Equal("Inscrição indevida em cadastro de inadimplentes", theme.GetProperty("name").GetString());
        Assert.Equal("CONSUMIDOR", theme.GetProperty("subjectArea").GetString());
        Assert.Equal(144, theme.GetProperty("judgedCount").GetInt64());
        Assert.Equal(78, theme.GetProperty("strengthScore").GetInt32());
        Assert.Equal("Dominante", theme.GetProperty("level").GetString());

        var outcome = theme.GetProperty("outcome");
        Assert.Equal(142, outcome.GetProperty("upheld").GetInt64());
        Assert.Equal(2, outcome.GetProperty("rejected").GetInt64());
        Assert.Equal(0.9861m, outcome.GetProperty("upheldRatio").GetDecimal());
        Assert.Equal("acolhimento da pretensão do autor", outcome.GetProperty("polarityLabel").GetString());
        Assert.Equal(2021, theme.GetProperty("periodStartYear").GetInt32());
        Assert.Equal(2026, theme.GetProperty("periodEndYear").GetInt32());
        Assert.Equal("2026-08-30", theme.GetProperty("lastDecisionDate").GetString());
    }

    [Fact]
    public async Task Returns_the_query_and_total_in_the_envelope()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao indevida", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync("/api/themes?q=inscricao+indevida");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("inscricao indevida", body.GetProperty("query").GetString());
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Returns_no_results_as_an_empty_list()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("satelite", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=satelite");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Empty(body.GetProperty("themes").EnumerateArray());
    }

    [Fact]
    public async Task Returns_no_top_themes_as_an_empty_list()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(7, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?limit=7");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Empty(body.GetProperty("themes").EnumerateArray());
    }

    [Fact]
    public async Task Defaults_the_limit_to_20_when_absent()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=inscricao");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync("inscricao", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Rejects_a_limit_outside_1_to_100(int limit)
    {
        var response = await _client.GetAsync($"/api/themes?limit={limit}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("O parâmetro 'limit' deve estar entre 1 e 100.", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("  a  ")]
    public async Task Rejects_a_query_shorter_than_3_characters(string query)
    {
        var response = await _client.GetAsync($"/api/themes?q={Uri.EscapeDataString(query)}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", body.GetProperty("type").GetString());
        Assert.Equal("Requisição inválida", body.GetProperty("title").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Digite ao menos 3 caracteres para buscar.", body.GetProperty("detail").GetString());
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync(query, It.IsAny<int>(), It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Searches_a_query_with_3_characters()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("abc", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync("abc", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("/api/themes?limit=7", null, 7)]
    [InlineData("/api/themes?q=&limit=8", "", 8)]
    [InlineData("/api/themes?q=%20%20%20&limit=9", "   ", 9)]
    public async Task Returns_the_top_themes_when_the_query_is_empty(string url, string? query, int limit)
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(limit, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", body.GetProperty("query").GetString());
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal(412, body.GetProperty("themes")[0].GetProperty("themeKey").GetInt64());
        factory.ThemeSearchReader.Verify(
            reader => reader.TopThemesAsync(limit, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Once);
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync(query, It.IsAny<int>(), It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Defaults_the_top_themes_limit_to_20_when_absent()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.ThemeSearchReader.Verify(
            reader => reader.TopThemesAsync(20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Passes_the_filters_to_the_search()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync(
                "cobranca filtrada",
                20,
                It.Is<ThemeFilters>(filters =>
                    filters.Courts!.SequenceEqual(new[] { "TJSP", "TJRJ" }) &&
                    filters.PeriodYears == 5 &&
                    filters.Levels!.SequenceEqual(new[] { "First", "SpecialCourt" }) &&
                    filters.MinStrength == 60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync(
            "/api/themes?q=cobranca%20filtrada&court=TJSP&court=TJRJ&period=5y&level=first&minStrength=60");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("/api/themes?limit=31&period=2y&level=second", null, 2, new[] { "Second", "AppealPanel" }, null)]
    [InlineData("/api/themes?limit=32&period=all&level=superior", null, null, new[] { "Superior" }, null)]
    [InlineData("/api/themes?limit=33&court=TJMG&minStrength=0", new[] { "TJMG" }, null, null, 0)]
    public async Task Passes_the_filters_to_the_top_themes(
        string url, string[]? courts, int? periodYears, string[]? levels, int? minStrength)
    {
        var limit = int.Parse(url.Split("limit=")[1].Split('&')[0]);
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(
                limit,
                It.Is<ThemeFilters>(filters =>
                    (courts == null ? filters.Courts == null : filters.Courts!.SequenceEqual(courts)) &&
                    filters.PeriodYears == periodYears &&
                    (levels == null ? filters.Levels == null : filters.Levels!.SequenceEqual(levels)) &&
                    filters.MinStrength == minStrength),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("/api/themes?period=10y", "Período inválido: use 5y, 2y ou all.")]
    [InlineData("/api/themes?q=cobranca&period=5", "Período inválido: use 5y, 2y ou all.")]
    [InlineData("/api/themes?level=terceiro", "Grau inválido: use first, second ou superior.")]
    [InlineData("/api/themes?q=cobranca&level=First", "Grau inválido: use first, second ou superior.")]
    [InlineData("/api/themes?minStrength=-1", "O parâmetro 'minStrength' deve estar entre 0 e 100.")]
    [InlineData("/api/themes?q=cobranca&minStrength=101", "O parâmetro 'minStrength' deve estar entre 0 e 100.")]
    public async Task Rejects_an_invalid_filter_value(string url, string detail)
    {
        var response = await _client.GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(detail, body.GetProperty("detail").GetString());
    }

    private static void AssertListsTheCourtCounts(JsonElement filterOptions)
    {
        var courts = filterOptions.GetProperty("courts").EnumerateArray().ToArray();
        Assert.Equal(["TJSP", "TJMG", "TJRJ"], courts.Select(court => court.GetProperty("code").GetString()));
        Assert.Equal([5412L, 2108L, 0L], courts.Select(court => court.GetProperty("caseCount").GetInt64()));
        Assert.Equal("Tribunal de Justiça de São Paulo", courts[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Returns_the_court_counts_with_the_search()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("contagem", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=contagem");

        AssertListsTheCourtCounts(body.GetProperty("filterOptions"));
    }

    [Fact]
    public async Task Returns_the_court_counts_with_the_top_themes()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(41, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?limit=41");

        AssertListsTheCourtCounts(body.GetProperty("filterOptions"));
    }

    [Fact]
    public async Task Offers_only_the_loaded_levels_with_the_search()
    {
        factory.CourtLevelReader.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["First", "AppealPanel"]);
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("graus", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?q=graus");

        var levels = body.GetProperty("filterOptions").GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(["first", "second"], levels.Select(level => level.GetProperty("code").GetString()));
        Assert.Equal(["Primeiro grau", "Segundo grau"], levels.Select(level => level.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Offers_the_superior_level_with_the_top_themes_when_it_is_loaded()
    {
        factory.CourtLevelReader.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["Second", "Superior"]);
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(42, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?limit=42");

        var levels = body.GetProperty("filterOptions").GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(["second", "superior"], levels.Select(level => level.GetProperty("code").GetString()));
        Assert.Equal("Superior", levels[1].GetProperty("label").GetString());
    }

    [Fact]
    public async Task Leaves_out_the_superior_level_when_only_state_courts_are_loaded()
    {
        factory.CourtLevelReader.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["First", "Second", "SpecialCourt"]);
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(43, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes?limit=43");

        var levels = body.GetProperty("filterOptions").GetProperty("levels").EnumerateArray();
        Assert.DoesNotContain(levels, level => level.GetProperty("code").GetString() == "superior");
    }
}
