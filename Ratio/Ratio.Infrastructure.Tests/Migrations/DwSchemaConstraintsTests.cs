using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class DwSchemaConstraintsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Court =
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); ";

    private const string Case = Court +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT '0000001-00.2024.8.26.0100', court_sk, 'First', 'datajud', now() FROM dw.dim_court; " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label) VALUES ('Granted', 'Procedente'); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk) SELECT 219, 'Procedência', outcome_sk FROM dw.dim_decision_outcome;";

    private const string Event =
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT c.case_sk, c.court_sk, m.movement_sk, now(), 'https://example.org', now(), 'datajud:1:219' " +
        "FROM dw.dim_case c CROSS JOIN dw.dim_movement m;";

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

    private async Task AssertRejectedAsync(string sql, string sqlState)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));
        Assert.Equal(sqlState, exception.SqlState);
    }

    [Fact]
    public Task Accepts_the_default_strength_configuration() =>
        ExecuteAsync("INSERT INTO dw.strength_config (methodology_version) VALUES ('1.0')");

    [Fact]
    public Task Rejects_strength_weights_that_do_not_add_up_to_one() =>
        AssertRejectedAsync(
            "INSERT INTO dw.strength_config (weight_agreement, methodology_version) VALUES (0.50, '1.0')",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public Task Keeps_a_single_strength_configuration() =>
        AssertRejectedAsync(
            "INSERT INTO dw.strength_config (id, methodology_version) VALUES (2, '1.0')",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public Task Rejects_a_percentage_floor_below_one() =>
        AssertRejectedAsync(
            "INSERT INTO dw.strength_config (methodology_version, min_judged_for_percentage) VALUES ('1.0', 0)",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public async Task Rejects_the_same_event_twice()
    {
        await ExecuteAsync(Case + Event);

        await AssertRejectedAsync(Event, PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public Task Rejects_a_verified_movement_without_polarity() =>
        AssertRejectedAsync(
            "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label) VALUES ('Denied', 'Improcedente'); " +
            "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified) " +
            "SELECT 220, 'Improcedência', outcome_sk, true FROM dw.dim_decision_outcome",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public Task Rejects_a_source_link_without_its_type() =>
        AssertRejectedAsync(
            Court +
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at, source_link) " +
            "SELECT '0000001-00.2024.8.26.0100', court_sk, 'First', 'datajud', now(), 'https://esaj.tjsp.jus.br' FROM dw.dim_court",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public Task Rejects_doctrine_without_doi_or_url() =>
        AssertRejectedAsync(
            "INSERT INTO dw.dim_doctrine (title, source, extracted_at) VALUES ('Sem link', 'scielo', now())",
            PostgresErrorCodes.CheckViolation);

    [Fact]
    public async Task Rejects_the_same_article_from_the_same_source_twice()
    {
        const string article =
            "INSERT INTO dw.dim_doctrine (title, article_url, source, extracted_at) VALUES ('Artigo', 'https://example.org/a', 'scielo', now())";
        await ExecuteAsync(article);

        await AssertRejectedAsync(article, PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Rejects_two_themes_with_the_same_key()
    {
        await ExecuteAsync("INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema A', 42)");

        await AssertRejectedAsync(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema B', 42)",
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public Task Rejects_a_theme_outside_the_known_areas() =>
        AssertRejectedAsync(
            "INSERT INTO dw.dim_theme (theme_name, theme_key, subject_area) VALUES ('Tema', 1, 'PENAL')",
            PostgresErrorCodes.CheckViolation);
}
