using Dapper;
using DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using System.Globalization;

namespace Ratio.Infrastructure.Tests.Migrations;

public sealed class IncrementalUpgradeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ScriptPrefix = "Ratio.Infrastructure.Migrations.Scripts.";

    [Fact]
    public async Task Applies_pending_migrations_and_preserves_existing_data()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await ApplyMigrationsThroughAsync(connectionString, 24);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_theme (theme_name, subject_area, theme_key) VALUES (@Name, @Area, @Key)",
                new { Name = "Existing theme", Area = "CIVIL", Key = 900001L });
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, thesis, status, source_url, extracted_at) " +
                "VALUES ('Tema', 952, 'O reajuste por faixa etária é válido.', 'Trânsito em Julgado', 'https://stj.jus.br', now())");
        }

        var appliedScripts = await new DatabaseMigrator(
            connectionString,
            NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);

        Assert.Equal(["Ratio.Infrastructure.Migrations.Scripts.V025__qualified_precedent_attributes.sql"], appliedScripts);

        await using var verification = new NpgsqlConnection(connectionString);
        await verification.OpenAsync();

        var preservedRows = await verification.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dw.dim_theme WHERE theme_key = 900001");
        var journalRows = await verification.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM migrations.schema_versions WHERE scriptname = 'Ratio.Infrastructure.Migrations.Scripts.V025__qualified_precedent_attributes.sql'");
        var precedentType = await verification.ExecuteScalarAsync<string>(
            "SELECT precedent_type FROM dw.dim_precedent WHERE precedent_kind = 'Tema' AND precedent_number = 952");

        Assert.Equal(1, preservedRows);
        Assert.Equal(1, journalRows);
        Assert.Equal("tema_repetitivo", precedentType);
    }

    private static async Task ApplyMigrationsThroughAsync(string connectionString, int version)
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS migrations");
        }

        await Task.Run(() =>
        {
            var result = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .JournalToPostgresqlTable("migrations", "schema_versions")
                .WithScriptsEmbeddedInAssembly(
                    typeof(DatabaseMigrator).Assembly,
                    name => IsMigrationThroughVersion(name, version))
                .WithTransactionPerScript()
                .WithVariablesDisabled()
                .LogToNowhere()
                .Build()
                .PerformUpgrade();

            if (!result.Successful)
            {
                throw new InvalidOperationException(result.Error?.Message, result.Error);
            }
        });
    }

    private static bool IsMigrationThroughVersion(string name, int version)
    {
        if (!name.StartsWith(ScriptPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = name[ScriptPrefix.Length..];
        var migrationVersion = int.Parse(fileName.AsSpan(1, 3), CultureInfo.InvariantCulture);
        return migrationVersion <= version;
    }
}
