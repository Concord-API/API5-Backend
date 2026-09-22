using Dapper;
using DbUp;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Migrations;

public sealed class DatabaseMigrator(string connectionString, ILogger<DatabaseMigrator> logger) : IDatabaseMigrator
{
    private const string JournalSchema = "migrations";
    private const string JournalTable = "schema_versions";
    private const long LockKey = 7_265_646_900_120;

    private static readonly string ScriptPrefix = $"{typeof(DatabaseMigrator).Namespace}.Scripts.";

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await lockConnection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(@LockKey)", new { LockKey }, cancellationToken: cancellationToken));
        try
        {
            await lockConnection.ExecuteAsync(new CommandDefinition(
                $"CREATE SCHEMA IF NOT EXISTS {JournalSchema}", cancellationToken: cancellationToken));
            return Upgrade();
        }
        finally
        {
            await lockConnection.ExecuteAsync("SELECT pg_advisory_unlock(@LockKey)", new { LockKey });
        }
    }

    private IReadOnlyList<string> Upgrade()
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable(JournalSchema, JournalTable)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly, name => name.StartsWith(ScriptPrefix, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogCritical(result.Error, "A migration {Script} falhou.", result.ErrorScript?.Name);
            throw new InvalidOperationException($"A migration {result.ErrorScript?.Name} falhou.", result.Error);
        }

        var applied = result.Scripts.Select(script => script.Name).ToArray();
        foreach (var script in applied)
        {
            logger.LogInformation("Migration aplicada: {Script}", script);
        }

        return applied;
    }
}
