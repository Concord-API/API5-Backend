using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeCourtAlignmentReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;
    private ThemeDetailReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ThemeDetailReader(_dataSource, NullLogger<ThemeDetailReader>.Instance);
        await ExecuteAsync("""
            INSERT INTO dw.strength_config (methodology_version, reference_year, min_judged_for_percentage)
            VALUES ('1.0', 2026, 3);
            INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES
                ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'),
                ('STJ', 'Superior Tribunal de Justiça', 'DF');
            INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema de alinhamento', 1);
            INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto', 101);
            INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk)
            SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s;
            INSERT INTO dw.dim_case_class (class_name, claimant_type)
            VALUES ('Ação de cobrança', 'autor_particular');
            INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES
                ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true);
            INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference)
            SELECT seed.code, seed.name, outcome.outcome_sk, true, seed.polarity
            FROM (VALUES
                (1, 'Procedência', 'Granted', 'pretensao_autor'),
                (2, 'Improcedência', 'Denied', 'pretensao_autor'),
                (3, 'Provimento', 'Granted', 'pretensao_recorrente'),
                (4, 'Não-provimento', 'Denied', 'pretensao_recorrente')) AS seed(code, name, outcome, polarity)
            JOIN dw.dim_decision_outcome outcome ON outcome.outcome_code = seed.outcome;
            INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day)
            VALUES (20260101, '2026-01-01', 2026, 1, 1, 'Janeiro', 1);
            INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at)
            SELECT seed.case_number, court.court_sk, class.case_class_sk, 'First', 'datajud', now()
            FROM (VALUES
                ('TJSP-1', 'TJSP'), ('TJSP-2', 'TJSP'), ('TJSP-3', 'TJSP'), ('TJSP-4', 'TJSP'),
                ('STJ-1', 'STJ'), ('STJ-2', 'STJ'), ('STJ-3', 'STJ'), ('STJ-4', 'STJ')) AS seed(case_number, court_code)
            JOIN dw.dim_court court ON court.court_code = seed.court_code
            CROSS JOIN dw.dim_case_class class;
            INSERT INTO dw.bridge_case_subject (case_sk, subject_sk)
            SELECT case_row.case_sk, subject.subject_sk
            FROM dw.dim_case case_row CROSS JOIN dw.dim_subject subject;
            INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key)
            SELECT case_row.case_sk, case_row.court_sk, movement.movement_sk, 20260101,
                   TIMESTAMPTZ '2026-01-01', 'https://example.org', now(), case_row.case_number
            FROM dw.dim_case case_row
            JOIN dw.dim_movement movement ON movement.movement_code = CASE
                WHEN case_row.case_number IN ('TJSP-1', 'TJSP-2', 'TJSP-3') THEN 1
                WHEN case_row.case_number = 'TJSP-4' THEN 2
                WHEN case_row.case_number IN ('STJ-1', 'STJ-2') THEN 3
                ELSE 4
            END;
            REFRESH MATERIALIZED VIEW dw.case_current_result;
            REFRESH MATERIALIZED VIEW dw.theme_summary;
            REFRESH MATERIALIZED VIEW dw.theme_strength;
            REFRESH MATERIALIZED VIEW dw.theme_outcome_distribution;
            REFRESH MATERIALIZED VIEW dw.theme_by_court;
            """);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Exposes_state_court_merit_alignment_and_superior_court_appeal_alignment()
    {
        var detail = await _reader.GetThemeAsync(1, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(
            [
                new CourtAlignment("STJ", 4, 0.5m, "provimento do recurso (pretensão do recorrente)"),
                new CourtAlignment("TJSP", 4, 0.75m, "acolhimento da pretensão do autor")
            ],
            detail.CourtAlignments);
        Assert.Equal(
            detail.JudgedCount,
            detail.CourtAlignments
                .Where(alignment => alignment.CourtCode is not "STJ" and not "STF")
                .Sum(alignment => alignment.N));
    }

    [Fact]
    public async Task Never_adds_merit_decisions_to_the_superior_court_row()
    {
        await ExecuteAsync("""
            INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at)
            SELECT 'STJ-MERIT', court.court_sk, class.case_class_sk, 'Superior', 'datajud', now()
            FROM dw.dim_court court CROSS JOIN dw.dim_case_class class WHERE court.court_code = 'STJ';
            INSERT INTO dw.bridge_case_subject (case_sk, subject_sk)
            SELECT case_row.case_sk, subject.subject_sk
            FROM dw.dim_case case_row CROSS JOIN dw.dim_subject subject WHERE case_row.case_number = 'STJ-MERIT';
            INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key)
            SELECT case_row.case_sk, case_row.court_sk, movement.movement_sk, 20260101,
                   TIMESTAMPTZ '2026-01-01', 'https://example.org', now(), case_row.case_number
            FROM dw.dim_case case_row JOIN dw.dim_movement movement ON movement.movement_code = 1
            WHERE case_row.case_number = 'STJ-MERIT';
            REFRESH MATERIALIZED VIEW dw.case_current_result;
            REFRESH MATERIALIZED VIEW dw.theme_summary;
            REFRESH MATERIALIZED VIEW dw.theme_by_court;
            """);

        var detail = await _reader.GetThemeAsync(1, CancellationToken.None);

        var superior = Assert.Single(detail!.CourtAlignments, alignment => alignment.CourtCode == "STJ");
        Assert.Equal(4, superior.N);
        Assert.Equal(0.5m, superior.Percentage);
        Assert.Equal("provimento do recurso (pretensão do recorrente)", superior.PolarityLabel);
    }

    [Fact]
    public async Task Shows_no_percentage_for_a_superior_court_below_the_minimum()
    {
        await ExecuteAsync("""
            UPDATE dw.strength_config SET min_judged_for_percentage = 5;
            """);

        var detail = await _reader.GetThemeAsync(1, CancellationToken.None);

        var superior = Assert.Single(detail!.CourtAlignments, alignment => alignment.CourtCode == "STJ");
        Assert.Equal(4, superior.N);
        Assert.Null(superior.Percentage);
    }
}
