using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public sealed class MigrationVersionTests
{
    [Fact]
    public void Rejects_repeated_migration_versions()
    {
        var repeatedVersions = MigrationVersions()
            .GroupBy(ExtractVersion)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(repeatedVersions);
    }

    private static IEnumerable<string> MigrationVersions() =>
        typeof(DatabaseMigrator).Assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.Scripts.V", StringComparison.Ordinal))
            .Select(name => name[(name.LastIndexOf(".V", StringComparison.Ordinal) + 1)..]);

    private static string ExtractVersion(string scriptName) => scriptName[..4];
}
