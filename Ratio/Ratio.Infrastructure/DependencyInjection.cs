using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<ILastExtractionReader, LastExtractionReader>();
        return services;
    }
}
