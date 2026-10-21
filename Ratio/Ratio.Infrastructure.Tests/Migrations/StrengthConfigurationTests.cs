using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class StrengthConfigurationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record StrengthSettings(
        short CoverageCourts,
        short MinJudgedForGrade,
        short DivergencePoints,
        short MinJudgedForDivergence);

    private sealed record ColumnMetadata(string ColumnName, string IsNullable);

    [Fact]
    public async Task Provides_the_grade_divergence_and_coverage_defaults()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.strength_config (methodology_version) VALUES ('1.0')");

        var settings = await connection.QuerySingleAsync<StrengthSettings>(
            "SELECT coverage_courts AS CoverageCourts, min_judged_for_grade AS MinJudgedForGrade, " +
            "divergence_points AS DivergencePoints, min_judged_for_divergence AS MinJudgedForDivergence " +
            "FROM dw.strength_config");

        Assert.Equal(new StrengthSettings(5, 10, 20, 5), settings);
    }

    [Fact]
    public async Task Keeps_the_new_strength_settings_required()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(database);

        var columns = await connection.QueryAsync<ColumnMetadata>(
            "SELECT column_name AS ColumnName, is_nullable AS IsNullable FROM information_schema.columns " +
            "WHERE table_schema = 'dw' AND table_name = 'strength_config' " +
            "AND column_name IN ('min_judged_for_grade', 'divergence_points', 'min_judged_for_divergence')");

        Assert.Equal(3, columns.Count());
        Assert.All(columns, column => Assert.Equal("NO", column.IsNullable));
    }
}
