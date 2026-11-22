using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Application.Tests.Cases;

public class CaseViewTests
{
    private const string Number = "1000001-23.2024.8.26.0100";
    private const string SiteUrl = "https://esaj.tjsp.jus.br/cpopg/show.do?processo.codigo=1";
    private static readonly DateTimeOffset ExtractedAt = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly CaseFields AllFields = new(
        "Procedimento Comum Cível",
        ["Planos de Saúde"],
        "5ª Vara Cível",
        "Maria Souza",
        new DateOnly(2024, 1, 10),
        new DateOnly(2025, 3, 5),
        "Procedente",
        "Arquivado");

    private static readonly IReadOnlyList<CaseMovement> Movements =
    [
        new(new DateOnly(2025, 3, 5), "Julgada procedente a ação"),
        new(new DateOnly(2024, 1, 10), "Distribuído livremente")
    ];

    private static CaseSiteRecord Found(string court = "TJSP", CaseFields? fields = null) =>
        new(Number, court, false, "found", fields ?? AllFields, Movements, SiteUrl, ExtractedAt);

    [Fact]
    public void Shows_every_published_field_and_the_movements_in_the_site_order()
    {
        var view = CaseView.From(Found());

        Assert.Equal(CaseAvailability.Available, view.Status);
        Assert.Equal(Number, view.CaseNumber);
        Assert.Equal("TJSP", view.Court);
        Assert.Equal(AllFields, view.Fields);
        Assert.Equal(Movements, view.Movements);
        Assert.Empty(view.MissingFields);
        Assert.Null(view.Message);
        Assert.Equal(new CaseSource("TJSP, consulta pública do e-SAJ", SiteUrl, ExtractedAt), view.Source);
    }

    [Fact]
    public void Flags_the_fields_the_site_does_not_publish_and_leaves_them_empty()
    {
        var fields = AllFields with { Subjects = [], Judge = null, DecisionResult = null };

        var view = CaseView.From(Found(fields: fields));

        Assert.Equal(CaseAvailability.Available, view.Status);
        Assert.Equal(fields, view.Fields);
        Assert.Equal(["subjects", "judge", "decisionResult"], view.MissingFields);
        Assert.Equal("O site do tribunal não publica estes campos para este processo.", view.Message);
    }

    [Fact]
    public void Exposes_nothing_of_a_case_sealed_in_the_sample()
    {
        var view = CaseView.From(Found() with { Sealed = true });

        AssertSealed(view);
    }

    [Fact]
    public void Exposes_nothing_of_a_case_the_site_shows_as_sealed()
    {
        var view = CaseView.From(Found() with { SiteStatus = "sealed", Fields = null, Movements = [] });

        AssertSealed(view);
    }

    [Theory]
    [InlineData("TJRJ", "Os dados deste processo não estão disponíveis no Ratio: a consulta pública do TJRJ só pode ser feita com captcha.")]
    [InlineData("TJMG", "Os dados deste processo não estão disponíveis no Ratio: a consulta pública do TJMG só pode ser feita com captcha.")]
    [InlineData("STJ", "Os dados deste processo não estão disponíveis no Ratio: a consulta processual do STJ bloqueia acesso automatizado.")]
    public void Returns_only_the_court_and_the_reason_for_a_court_without_a_source(string court, string message)
    {
        var view = CaseView.From(new CaseSiteRecord(Number, court, false, null, null, [], null, null));

        Assert.Equal(new CaseDetail(null, court, CaseAvailability.SourceUnavailable, message, null, [], [], null), view);
    }

    [Fact]
    public void Says_when_the_site_did_not_find_the_case()
    {
        var view = CaseView.From(Found() with { SiteStatus = "not_found", Fields = null, Movements = [] });

        Assert.Equal(CaseAvailability.NotFoundOnSite, view.Status);
        Assert.Equal(Number, view.CaseNumber);
        Assert.Null(view.Fields);
        Assert.Equal("A consulta pública do TJSP não encontrou este processo.", view.Message);
        Assert.Equal(new CaseSource("TJSP, consulta pública do e-SAJ", SiteUrl, ExtractedAt), view.Source);
    }

    [Fact]
    public void Says_when_the_case_was_not_collected_yet()
    {
        var view = CaseView.From(new CaseSiteRecord(Number, "TJSP", false, null, null, [], null, null));

        Assert.Equal(CaseAvailability.NotLoaded, view.Status);
        Assert.Equal(Number, view.CaseNumber);
        Assert.Null(view.Fields);
        Assert.Null(view.Source);
        Assert.Equal("Os dados deste processo ainda não foram coletados do site do tribunal; eles entram na próxima carga.", view.Message);
    }

    private static void AssertSealed(CaseDetail view) =>
        Assert.Equal(
            new CaseDetail(null, null, CaseAvailability.Sealed, "Este processo está em sigilo; o Ratio não expõe nenhum dado dele.", null, [], [], null),
            view);
}
