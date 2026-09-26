using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<ILastExtractionReader, LastExtractionReader>();
        services.AddSingleton<IThemeSearchReader, ThemeSearchReader>();
        services.AddSingleton<IThemeDetailReader, ThemeDetailReader>();
        services.AddSingleton<IProvenanceReader, ProvenanceReader>();
        services.AddSingleton<IDoctrineReader, DoctrineReader>();
        services.AddSingleton<IScopeReader, ScopeReader>();
        services.AddSingleton<IDatabaseMigrator>(provider =>
            new DatabaseMigrator(connectionString, provider.GetRequiredService<ILogger<DatabaseMigrator>>()));
        return services;
    }
}
