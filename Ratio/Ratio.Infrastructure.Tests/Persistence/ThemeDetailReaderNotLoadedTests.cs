using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeDetailReaderNotLoadedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Finds_no_theme_when_the_warehouse_was_never_loaded()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        await using var dataSource = NpgsqlDataSource.Create(database);
        await using (var command = dataSource.CreateCommand(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Cobrança de dívida', 1)"))
        {
            await command.ExecuteNonQueryAsync();
        }

        var reader = new ThemeDetailReader(dataSource, NullLogger<ThemeDetailReader>.Instance);

        Assert.Null(await reader.GetThemeAsync(1, CancellationToken.None));
    }
}
