using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Acceptance;

public class SampleLoadTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private NpgsqlDataSource _dataSource = null!;

    public SampleLoadTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        var database = await _postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task LoadSampleAsync()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "acceptance", "sample-load.sql"));
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Marks_the_warehouse_as_ready()
    {
        await LoadSampleAsync();

        var lastExtraction = await new LastExtractionReader(_dataSource, NullLogger<LastExtractionReader>.Instance)
            .GetLastExtractionAsync(CancellationToken.None);

        Assert.Equal(WarehouseAvailability.Ready, lastExtraction.Availability);
    }

    [Fact]
    public async Task Finds_a_judged_theme_by_search()
    {
        await LoadSampleAsync();

        var results = await new ThemeSearchReader(_dataSource, NullLogger<ThemeSearchReader>.Instance)
            .SearchThemesAsync("cobranca de divida", 20, CancellationToken.None);

        var theme = Assert.Single(results);
        Assert.True(theme.JudgedCount > 0);
        Assert.NotNull(theme.StrengthScore);
    }

    [Fact]
    public async Task Lists_the_top_themes()
    {
        await LoadSampleAsync();

        var results = await new ThemeSearchReader(_dataSource, NullLogger<ThemeSearchReader>.Instance)
            .TopThemesAsync(20, CancellationToken.None);

        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task Reads_theme_alignment_by_court_after_sample_load()
    {
        await LoadSampleAsync();

        var detail = await new ThemeDetailReader(_dataSource, NullLogger<ThemeDetailReader>.Instance)
            .GetThemeAsync(1, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Contains(detail.CourtAlignments, alignment => alignment.CourtCode == "TJSP");
    }

    [Fact]
    public async Task Reads_the_panels_of_a_loaded_theme()
    {
        await LoadSampleAsync();

        var counts = await new PanelAlignmentReader(_dataSource).GetThemeAsync(1, CancellationToken.None);

        Assert.NotNull(counts);
    }
}
