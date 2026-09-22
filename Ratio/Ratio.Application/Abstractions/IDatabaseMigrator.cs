namespace Ratio.Application.Abstractions;

public interface IDatabaseMigrator
{
    Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken);
}
