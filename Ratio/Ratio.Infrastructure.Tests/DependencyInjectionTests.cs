using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ratio.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=unused";

    [Fact]
    public async Task Disposes_the_data_source_when_the_container_shuts_down()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(UnreachableDatabase);
        var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await dataSource.OpenConnectionAsync());
    }
}
