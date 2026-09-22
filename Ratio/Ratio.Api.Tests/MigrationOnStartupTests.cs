using Moq;

namespace Ratio.Api.Tests;

public class MigrationOnStartupTests
{
    [Fact]
    public void Applies_the_migrations_when_the_api_starts()
    {
        using var factory = new ApiFactory();

        using var client = factory.CreateClient();

        factory.DatabaseMigrator.Verify(migrator => migrator.MigrateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Refuses_to_start_when_the_migration_fails()
    {
        using var factory = new ApiFactory();
        factory.DatabaseMigrator
            .Setup(migrator => migrator.MigrateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("A migration V001__dw_schema.sql falhou."));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("V001__dw_schema.sql", exception.ToString());
    }
}
