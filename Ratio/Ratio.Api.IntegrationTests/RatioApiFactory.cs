using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Ratio.Api.IntegrationTests;

public sealed class RatioApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Ratio", connectionString);
    }

    public async Task LoadSampleAsync()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "acceptance", "sample-load.sql"));
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}
