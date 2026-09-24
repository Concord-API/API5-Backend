using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeDetailReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string WarehouseData =
        "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied'; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de indenização', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20240615, '2024-06-15', 2024, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key, subject_area) VALUES ('Inscrição indevida em cadastro de inadimplentes', 412, 'CONSUMIDOR'); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Inscrição indevida', 1001); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
        "SELECT 'DETAIL-' || lpad(n::text, 6, '0'), c.court_sk, cc.case_class_sk, 'First', 'datajud', now() " +
        "FROM generate_series(1, 145) n CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) SELECT c.case_sk, s.subject_sk FROM dw.dim_case c CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT c.case_sk, c.court_sk, m.movement_sk, 20240615, TIMESTAMPTZ '2024-06-15', 'https://example.org', now(), 'detail:' || c.case_sk " +
        "FROM dw.dim_case c JOIN dw.dim_movement m ON m.movement_code = CASE WHEN right(c.case_number, 6)::integer <= 142 THEN 219 ELSE 220 END " +
        "WHERE right(c.case_number, 6)::integer <= 144;";

    private const string Narrative =
        "INSERT INTO dw.theme_narrative (theme_sk, lead, body, text_origin, methodology_version, generated_at) " +
        "SELECT theme_sk, " +
        "'[{\"text\":\"Em \"},{\"ratio\":0.9861,\"n\":144,\"unit\":\"decisões\"},{\"text\":\" julgadas, houve acolhimento da pretensão do autor.\"}]'::jsonb, " +
        "'[{\"text\":\"As decisões vêm de 1 tribunal, em 2024.\"}]'::jsonb, " +
        "'template', '1.0', DATE '2026-09-23' FROM dw.dim_theme WHERE theme_key = 412;";

    private NpgsqlDataSource _dataSource = null!;
    private ThemeDetailReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ThemeDetailReader(_dataSource);
        await ExecuteAsync(WarehouseData);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_strength");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Reads_the_theme_header_and_narrative_by_public_key()
    {
        await ExecuteAsync(Narrative);

        var detail = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(412, detail.ThemeKey);
        Assert.Equal("Inscrição indevida em cadastro de inadimplentes", detail.Name);
        Assert.Equal("CONSUMIDOR", detail.SubjectArea);
        Assert.Equal(80, detail.StrengthScore);
        Assert.Equal("Dominante", detail.Level);
        Assert.Equal(145, detail.CaseCount);
        Assert.Equal(144, detail.JudgedCount);
        Assert.Equal(1, detail.CourtCount);
        Assert.Equal(2024, detail.PeriodStartYear);
        Assert.Equal(2024, detail.PeriodEndYear);
        Assert.Equal(new DateOnly(2024, 6, 15), detail.LastDecisionDate);

        var summary = detail.Summary;
        Assert.NotNull(summary);
        Assert.Equal("Em ", summary.Lead[0].GetProperty("text").GetString());
        Assert.Equal(0.9861m, summary.Lead[1].GetProperty("ratio").GetDecimal());
        Assert.Equal(144, summary.Lead[1].GetProperty("n").GetInt64());
        Assert.Equal("decisões", summary.Lead[1].GetProperty("unit").GetString());
        Assert.Equal("As decisões vêm de 1 tribunal, em 2024.", summary.Body[0].GetProperty("text").GetString());
        Assert.Equal("template", summary.TextOrigin);
        Assert.Equal("1.0", summary.MethodologyVersion);
        Assert.Equal(new DateOnly(2026, 9, 23), summary.GeneratedAt);
    }

    [Fact]
    public async Task Returns_a_null_summary_when_the_theme_has_no_narrative()
    {
        var detail = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail.Summary);
    }
}
