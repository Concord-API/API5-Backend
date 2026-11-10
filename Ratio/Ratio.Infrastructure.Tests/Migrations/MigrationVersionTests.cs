using Dapper;
using DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public sealed class MigrationVersionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void Rejects_repeated_migration_versions()
    {
        var repeatedVersions = MigrationVersions()
            .GroupBy(ExtractVersion)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Equal(["V006"], repeatedVersions);
        Assert.Equal(
            ["V006__order_theme_search_by_strength.sql", "V006__search_synonyms.sql"],
            MigrationVersions().Where(script => script.StartsWith("V006", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task Applies_pending_scripts_when_the_journal_contains_the_first_v006()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS migrations");
        }

        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable("migrations", "schema_versions")
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                name => name.EndsWith("V006__order_theme_search_by_strength.sql", StringComparison.Ordinal)
                    || Enumerable.Range(1, 5).Any(version => name.Contains($".V{version:000}__", StringComparison.Ordinal)))
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        Assert.True(result.Successful);

        var applied = await new DatabaseMigrator(
            connectionString,
            NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);

        Assert.Contains(
            "Ratio.Infrastructure.Migrations.Scripts.V006__search_synonyms.sql",
            applied);

        await using var verification = new NpgsqlConnection(connectionString);
        var journalCount = await verification.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM migrations.schema_versions");

        Assert.Equal(25, journalCount);
    }

    private static IEnumerable<string> MigrationVersions() =>
        typeof(DatabaseMigrator).Assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.Scripts.V", StringComparison.Ordinal))
            .Select(name => name[(name.LastIndexOf(".V", StringComparison.Ordinal) + 1)..]);

    private static string ExtractVersion(string scriptName) => scriptName[..4];
}
