using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

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

    [Fact]
    public void Caps_the_connection_pool_at_40_when_the_connection_string_does_not_set_it()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        Assert.Equal(40, new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).MaxPoolSize);
    }

    [Fact]
    public void Keeps_the_pool_size_set_in_the_connection_string()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(UnreachableDatabase + ";Maximum Pool Size=10");
        using var provider = services.BuildServiceProvider();

        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        Assert.Equal(10, new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).MaxPoolSize);
    }

    [Fact]
    public void Registers_the_database_migrator()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DatabaseMigrator>(provider.GetRequiredService<IDatabaseMigrator>());
    }

    [Fact]
    public void Registers_the_theme_detail_reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ThemeDetailReader>(provider.GetRequiredService<IThemeDetailReader>());
    }

    [Fact]
    public void Registers_the_provenance_reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ProvenanceReader>(provider.GetRequiredService<IProvenanceReader>());
    }

    [Fact]
    public void Registers_the_doctrine_reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DoctrineReader>(provider.GetRequiredService<IDoctrineReader>());
    }

    [Fact]
    public void Registers_the_scope_reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ScopeReader>(provider.GetRequiredService<IScopeReader>());
    }

    [Fact]
    public void Registers_the_theme_cases_reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ThemeCasesReader>(provider.GetRequiredService<IThemeCasesReader>());
    }

    [Fact]
    public void Serves_the_court_levels_from_the_load_cache()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CachedCourtLevelReader>(provider.GetRequiredService<ICourtLevelReader>());
    }

    [Fact]
    public void Serves_the_court_case_counts_from_the_load_cache()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(UnreachableDatabase);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CachedCourtCaseCountReader>(provider.GetRequiredService<ICourtCaseCountReader>());
    }
}
