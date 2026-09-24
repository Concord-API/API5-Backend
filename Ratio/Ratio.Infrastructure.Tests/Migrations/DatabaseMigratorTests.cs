using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Migrations;

public class DatabaseMigratorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly string[] Tables =
    [
        "bridge_case_subject",
        "bridge_subject_doctrine",
        "bridge_theme_subject",
        "dim_case",
        "dim_case_class",
        "dim_court",
        "dim_date",
        "dim_decision_outcome",
        "dim_doctrine",
        "dim_judging_body",
        "dim_movement",
        "dim_subject",
        "dim_theme",
        "fact_case_event",
        "search_synonym",
        "strength_config",
        "theme_narrative"
    ];

    private static readonly string[] MaterializedViews = ["case_current_result", "data_provenance", "theme_strength", "theme_summary"];

    private static readonly string[] Scripts =
    [
        "V001__dw_schema.sql",
        "V002__portuguese_text_search.sql",
        "V003__theme_search_columns.sql",
        "V004__search_themes.sql",
        "V005__theme_strength.sql",
        "V006__order_theme_search_by_strength.sql",
        "V006__search_synonyms.sql",
        "V007__top_themes.sql",
        "V008__search_only_judged_themes.sql",
        "V009__break_strength_ties_by_volume.sql",
        "V010__theme_narrative.sql",
        "V012__data_provenance.sql"
    ];

    private static string[] ScriptFileNames(IEnumerable<string> names) =>
        names.Select(name => name[(name.LastIndexOf(".V", StringComparison.Ordinal) + 1)..]).ToArray();

    private static DatabaseMigrator Migrator(string connectionString) =>
        new(connectionString, NullLogger<DatabaseMigrator>.Instance);

    private static async Task<T[]> QueryAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return (await connection.QueryAsync<T>(sql)).ToArray();
    }

    [Fact]
    public async Task Creates_the_dw_schema_on_an_empty_database()
    {
        var database = await postgres.CreateDatabaseAsync();

        var applied = await Migrator(database).MigrateAsync(CancellationToken.None);

        Assert.Equal(Scripts, ScriptFileNames(applied));
        Assert.Equal(Tables, await QueryAsync<string>(database,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'dw' AND table_type = 'BASE TABLE' ORDER BY table_name"));
        Assert.Equal(MaterializedViews, await QueryAsync<string>(database,
            "SELECT matviewname FROM pg_matviews WHERE schemaname = 'dw' ORDER BY matviewname"));
    }

    [Fact]
    public async Task Creates_a_unique_index_on_every_materialized_view()
    {
        var database = await postgres.CreateDatabaseAsync();

        await Migrator(database).MigrateAsync(CancellationToken.None);

        Assert.Equal(MaterializedViews, await QueryAsync<string>(database,
            "SELECT DISTINCT tablename FROM pg_indexes WHERE schemaname = 'dw' AND indexdef LIKE 'CREATE UNIQUE INDEX%' " +
            "AND tablename IN (SELECT matviewname FROM pg_matviews WHERE schemaname = 'dw') ORDER BY tablename"));
    }

    [Fact]
    public async Task Records_applied_scripts_outside_the_dw_schema()
    {
        var database = await postgres.CreateDatabaseAsync();

        await Migrator(database).MigrateAsync(CancellationToken.None);

        var journal = await QueryAsync<string>(database, "SELECT scriptname FROM migrations.schema_versions ORDER BY schemaversionsid");
        Assert.Equal(Scripts, ScriptFileNames(journal));
    }

    [Fact]
    public async Task Applies_nothing_on_the_second_run()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Migrator(database).MigrateAsync(CancellationToken.None);

        var applied = await Migrator(database).MigrateAsync(CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(Scripts.Length, (await QueryAsync<string>(database, "SELECT scriptname FROM migrations.schema_versions")).Length);
    }

    [Fact]
    public async Task Applies_each_script_once_when_two_instances_start_together()
    {
        var database = await postgres.CreateDatabaseAsync();

        var runs = await Task.WhenAll(
            Migrator(database).MigrateAsync(CancellationToken.None),
            Migrator(database).MigrateAsync(CancellationToken.None));

        Assert.Equal(Scripts, ScriptFileNames(runs.SelectMany(applied => applied)));
        Assert.Equal(Scripts.Length, (await QueryAsync<string>(database, "SELECT scriptname FROM migrations.schema_versions")).Length);
    }

    [Fact]
    public async Task Leaves_the_warehouse_empty_until_the_load_file_runs()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Migrator(database).MigrateAsync(CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(database);
        var reader = new LastExtractionReader(dataSource, NullLogger<LastExtractionReader>.Instance);

        var result = await reader.GetLastExtractionAsync(CancellationToken.None);

        Assert.Equal(WarehouseAvailability.Empty, result.Availability);
    }

    [Fact]
    public async Task Fails_when_the_database_is_unreachable()
    {
        var migrator = Migrator("Host=127.0.0.1;Port=1;Database=unused;Timeout=2");

        await Assert.ThrowsAnyAsync<NpgsqlException>(() => migrator.MigrateAsync(CancellationToken.None));
    }
}
