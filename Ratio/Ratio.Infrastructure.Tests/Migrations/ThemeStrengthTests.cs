using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeStrengthTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record ThemeStrengthRow(
        int Score,
        string Level,
        decimal AgreementValue,
        decimal VolumeValue,
        decimal CoverageValue,
        decimal RecencyValue,
        string? AgreementBasis,
        long Judged,
        long Upheld,
        long Rejected,
        long Courts);

    private sealed record ThemeStrengthEdgeCaseRow(
        long ThemeKey,
        string AgreementBasis,
        decimal AgreementValue,
        decimal VolumeValue,
        decimal RecencyValue,
        long ClaimUpheldCount,
        long ClaimRejectedCount,
        long AppealUpheldCount,
        long AppealRejectedCount,
        long Upheld,
        long Rejected,
        string ClaimPolarityLabel);

    private const string Config = "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026);";

    private const string Outcomes =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied';";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RefreshAggregatesAsync()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_strength");
    }

    private async Task<ThemeStrengthRow> ReadStrengthAsync(long themeKey)
    {
        await using var connection = _dataSource.CreateConnection();
        return await connection.QuerySingleAsync<ThemeStrengthRow>(
            "SELECT ts.score AS Score, ts.level AS Level, ts.agreement_value AS AgreementValue, ts.volume_value AS VolumeValue, " +
            "ts.coverage_value AS CoverageValue, ts.recency_value AS RecencyValue, ts.agreement_basis AS AgreementBasis, " +
            "ts.judged AS Judged, ts.upheld AS Upheld, ts.rejected AS Rejected, ts.courts AS Courts " +
            "FROM dw.theme_strength ts JOIN dw.dim_theme t ON t.theme_sk = ts.theme_sk WHERE t.theme_key = @ThemeKey",
            new { ThemeKey = themeKey });
    }

    private async Task<long[]> QueryThemeKeysAsync(string sql)
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<long>(sql)).ToArray();
    }

    private async Task<ThemeStrengthEdgeCaseRow[]> ReadEdgeCasesAsync()
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<ThemeStrengthEdgeCaseRow>(
            "SELECT t.theme_key AS ThemeKey, s.agreement_basis AS AgreementBasis, s.agreement_value AS AgreementValue, " +
            "s.volume_value AS VolumeValue, s.recency_value AS RecencyValue, s.claim_upheld_count AS ClaimUpheldCount, " +
            "s.claim_rejected_count AS ClaimRejectedCount, s.appeal_upheld_count AS AppealUpheldCount, " +
            "s.appeal_rejected_count AS AppealRejectedCount, s.upheld AS Upheld, s.rejected AS Rejected, " +
            "s.claim_polarity_label AS ClaimPolarityLabel FROM dw.theme_strength s " +
            "JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk WHERE t.theme_key BETWEEN 3 AND 6 ORDER BY t.theme_key"))
            .ToArray();
    }

    [Fact]
    public async Task Recomputes_strength_and_preserves_boundary_case_semantics()
    {
        await ExecuteAsync(
            "INSERT INTO dw.strength_config (methodology_version, reference_year, volume_saturation) VALUES ('1.0', 2026, 10); " +
            "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) " +
            "VALUES ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
            "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
            "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
            "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
            "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied'; " +
            "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
            "SELECT 237, 'Provimento recursal', outcome_sk, true, 'pretensao_recorrente' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
            "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
            "SELECT 238, 'Desprovimento recursal', outcome_sk, true, 'pretensao_recorrente' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied'; " +
            "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
            "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
            "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
            "VALUES (20200615, '2020-06-15', 2020, 2, 6, 'Junho', 15), (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES " +
            "('Tema recursal', 3), ('Tema equilibrado', 4), ('Tema antigo', 5), ('Tema saturado', 6); " +
            "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES " +
            "('Assunto recursal', 103), ('Assunto equilibrado', 104), ('Assunto antigo', 105), ('Assunto saturado', 106); " +
            "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
            "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 100 WHERE t.theme_key BETWEEN 3 AND 6; " +
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
            "SELECT 'INTEGRITY-' || t.theme_key || '-' || series.case_index, c.court_sk, cc.case_class_sk, 'First', 'datajud', now() " +
            "FROM dw.dim_theme t CROSS JOIN LATERAL generate_series(1, CASE WHEN t.theme_key = 6 THEN 10 ELSE 2 END) AS series(case_index) " +
            "CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc WHERE t.theme_key BETWEEN 3 AND 6; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT c.case_sk, s.subject_sk FROM dw.dim_case c JOIN dw.dim_subject s " +
            "ON s.subject_code = split_part(c.case_number, '-', 2)::integer + 100 WHERE c.case_number LIKE 'INTEGRITY-%'; " +
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT c.case_sk, c.court_sk, m.movement_sk, " +
            "CASE WHEN split_part(c.case_number, '-', 2) = '5' THEN 20200615 ELSE 20250615 END, " +
            "CASE WHEN split_part(c.case_number, '-', 2) = '5' THEN TIMESTAMPTZ '2020-06-15' ELSE TIMESTAMPTZ '2025-06-15' END, " +
            "'https://example.org', now(), 'integrity:' || c.case_sk " +
            "FROM dw.dim_case c JOIN dw.dim_movement m ON m.movement_code = CASE " +
            "WHEN split_part(c.case_number, '-', 2) = '3' THEN CASE WHEN split_part(c.case_number, '-', 3) = '1' THEN 237 ELSE 238 END " +
            "WHEN split_part(c.case_number, '-', 2) IN ('4', '5') THEN CASE WHEN split_part(c.case_number, '-', 3) = '1' THEN 219 ELSE 220 END " +
            "ELSE 219 END WHERE c.case_number LIKE 'INTEGRITY-%';");

        await RefreshAggregatesAsync();

        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk " +
            "WHERE abs(s.score - round((s.agreement_value * s.agreement_weight + s.volume_value * s.volume_weight + " +
            "s.coverage_value * s.coverage_weight + s.recency_value * s.recency_weight) * 100)::integer) > 1"));
        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk WHERE s.level <> " +
            "CASE WHEN s.score >= 90 THEN 'Consolidada' WHEN s.score >= 75 THEN 'Dominante' " +
            "WHEN s.score >= 55 THEN 'Em formação' ELSE 'Divergente' END"));
        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk " +
            "WHERE abs(s.agreement_weight + s.volume_weight + s.coverage_weight + s.recency_weight - 1.0) >= 0.001"));
        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk WHERE " +
            "(s.claim_upheld_count + s.claim_rejected_count > 0 AND s.agreement_basis <> 'pretensao_autor') OR " +
            "(s.claim_upheld_count + s.claim_rejected_count = 0 AND s.appeal_upheld_count + s.appeal_rejected_count > 0 " +
            "AND s.agreement_basis <> 'pretensao_recorrente') OR " +
            "(s.claim_upheld_count + s.claim_rejected_count = 0 AND s.appeal_upheld_count + s.appeal_rejected_count = 0 " +
            "AND s.agreement_basis IS NOT NULL) OR " +
            "(s.agreement_basis = 'pretensao_autor' AND (s.upheld <> s.claim_upheld_count OR s.rejected <> s.claim_rejected_count)) OR " +
            "(s.agreement_basis = 'pretensao_recorrente' AND (s.upheld <> s.appeal_upheld_count OR s.rejected <> s.appeal_rejected_count)) OR " +
            "(s.agreement_basis IS NULL AND s.judged <> 0)"));
        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk " +
            "WHERE s.judged > 0 AND s.claim_polarity_label IS NULL"));
        Assert.Empty(await QueryThemeKeysAsync(
            "SELECT t.theme_key FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk WHERE " +
            "s.score NOT BETWEEN 0 AND 100 OR s.agreement_value NOT BETWEEN 0 AND 1 OR " +
            "s.volume_value NOT BETWEEN 0 AND 1 OR s.coverage_value NOT BETWEEN 0 AND 1 OR s.recency_value NOT BETWEEN 0 AND 1"));

        var rows = await ReadEdgeCasesAsync();

        Assert.Equal(4, rows.Length);
        var appealOnly = rows.Single(row => row.ThemeKey == 3);
        Assert.Equal("pretensao_recorrente", appealOnly.AgreementBasis);
        Assert.Equal(0, appealOnly.ClaimUpheldCount + appealOnly.ClaimRejectedCount);
        Assert.Equal(1, appealOnly.AppealUpheldCount);
        Assert.Equal(1, appealOnly.AppealRejectedCount);
        Assert.Equal(appealOnly.AppealUpheldCount, appealOnly.Upheld);
        Assert.Equal(appealOnly.AppealRejectedCount, appealOnly.Rejected);
        Assert.Equal("acolhimento da pretensão de quem recorreu", appealOnly.ClaimPolarityLabel);
        Assert.Equal(0m, rows.Single(row => row.ThemeKey == 4).AgreementValue);
        Assert.Equal(0m, rows.Single(row => row.ThemeKey == 5).RecencyValue);
        Assert.Equal(1m, rows.Single(row => row.ThemeKey == 6).VolumeValue);
    }

    [Fact]
    public async Task Scores_the_methodology_worked_example_as_dominant()
    {
        await ExecuteAsync(Config + Outcomes +
            "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
            "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
            "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20240615, '2024-06-15', 2024, 2, 6, 'Junho', 15); " +
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Cobrança de dívida', 1); " +
            "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Cobrança', 100); " +
            "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) SELECT theme_sk, subject_sk FROM dw.dim_theme, dw.dim_subject; " +
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
            "SELECT 'CASE-' || lpad(n::text, 6, '0'), c.court_sk, cc.case_class_sk, 'First', 'datajud', now() " +
            "FROM generate_series(1, 144) n CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20240615, TIMESTAMPTZ '2024-06-15', 'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_case dc JOIN dw.dim_movement m " +
            "ON m.movement_code = CASE WHEN right(dc.case_number, 6)::int <= 142 THEN 219 ELSE 220 END;");

        await RefreshAggregatesAsync();
        var strength = await ReadStrengthAsync(1);

        Assert.Equal(80, strength.Score);
        Assert.Equal("Dominante", strength.Level);
        Assert.Equal(0.972m, strength.AgreementValue);
        Assert.Equal(0.872m, strength.VolumeValue);
        Assert.Equal(0.333m, strength.CoverageValue);
        Assert.Equal(0.800m, strength.RecencyValue);
        Assert.Equal("pretensao_autor", strength.AgreementBasis);
        Assert.Equal(144, strength.Judged);
        Assert.Equal(142, strength.Upheld);
        Assert.Equal(2, strength.Rejected);
        Assert.Equal(1, strength.Courts);
    }

    [Fact]
    public async Task Scores_a_theme_without_judgments_as_zero_instead_of_dividing_by_zero()
    {
        await ExecuteAsync(Config +
            "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema sem julgamento', 2); " +
            "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto sem julgamento', 200); " +
            "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) SELECT theme_sk, subject_sk FROM dw.dim_theme, dw.dim_subject; " +
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT '0000002-00.2024.8.26.0100', court_sk, 'First', 'datajud', now() FROM dw.dim_court; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) SELECT c.case_sk, s.subject_sk FROM dw.dim_case c CROSS JOIN dw.dim_subject s;");

        await RefreshAggregatesAsync();
        var strength = await ReadStrengthAsync(2);

        Assert.Equal(0, strength.Score);
        Assert.Equal("Divergente", strength.Level);
        Assert.Equal(0m, strength.AgreementValue);
        Assert.Equal(0m, strength.VolumeValue);
        Assert.Equal(0m, strength.CoverageValue);
        Assert.Equal(0m, strength.RecencyValue);
        Assert.Null(strength.AgreementBasis);
        Assert.Equal(0, strength.Judged);
        Assert.Equal(0, strength.Courts);
    }
}
