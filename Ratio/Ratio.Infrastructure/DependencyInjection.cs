using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure;

public static class DependencyInjection
{
    private const int DefaultMaxPoolSize = 40;

    private static readonly string[] PoolSizeKeys = ["Maximum Pool Size", "Max Pool Size", "MaxPoolSize"];

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(WithPoolLimit(connectionString)));
        services.AddSingleton<ILastExtractionReader, LastExtractionReader>();
        services.AddSingleton<IThemeSearchReader, ThemeSearchReader>();
        services.AddSingleton<IThemeDetailReader, ThemeDetailReader>();
        services.AddSingleton<IProvenanceReader, ProvenanceReader>();
        services.AddSingleton<IDoctrineReader, DoctrineReader>();
        services.AddSingleton<IScopeReader, ScopeReader>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PublishedLoadVersion>();
        services.AddSingleton<CourtCaseCountReader>();
        services.AddSingleton<ICourtCaseCountReader, CachedCourtCaseCountReader>();
        services.AddSingleton<CourtLevelReader>();
        services.AddSingleton<ICourtLevelReader, CachedCourtLevelReader>();
        services.AddSingleton<IPanelAlignmentReader, PanelAlignmentReader>();
        services.AddSingleton<IThemeCasesReader, ThemeCasesReader>();
        services.AddSingleton<IPrecedentReader, PrecedentReader>();
        services.AddSingleton<IRulingReader, RulingReader>();
        services.AddSingleton<ICaseDetailReader, CaseDetailReader>();
        services.AddSingleton<IDatabaseMigrator>(provider =>
            new DatabaseMigrator(connectionString, provider.GetRequiredService<ILogger<DatabaseMigrator>>()));
        return services;
    }

    private static string WithPoolLimit(string connectionString)
    {
        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (PoolSizeKeys.Any(given.ContainsKey))
        {
            return connectionString;
        }

        return new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = DefaultMaxPoolSize }.ConnectionString;
    }
}
