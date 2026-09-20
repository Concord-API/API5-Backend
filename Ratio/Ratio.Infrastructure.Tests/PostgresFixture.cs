using Npgsql;
using Testcontainers.PostgreSql;

namespace Ratio.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithEnvironment("POSTGRES_INITDB_ARGS", "--locale-provider=icu --icu-locale=pt-BR --encoding=UTF8 --locale=C.utf8")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm; CREATE EXTENSION IF NOT EXISTS unaccent;");
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}
