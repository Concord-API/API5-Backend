using Ratio.Application.Abstractions;

namespace Ratio.Api.Hosting;

public sealed class DatabaseMigrationService(IDatabaseMigrator migrator, ILogger<DatabaseMigrationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await migrator.MigrateAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "A API não conseguiu subir: falha ao aplicar as migrations do banco.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
