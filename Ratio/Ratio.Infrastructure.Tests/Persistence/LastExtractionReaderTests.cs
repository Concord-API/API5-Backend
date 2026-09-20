using Microsoft.Extensions.Logging.Abstractions;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class LastExtractionReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly LastExtractionReader _reader = new(postgres.DataSource, NullLogger<LastExtractionReader>.Instance);

    public Task InitializeAsync() => postgres.ExecuteAsync("DROP SCHEMA IF EXISTS dw CASCADE");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reports_empty_when_the_dw_schema_does_not_exist()
    {
        var result = await _reader.GetLastExtractionAsync(CancellationToken.None);

        // Banco de pé, dump nunca restaurado: é base vazia, não banco fora do ar.
        Assert.Equal(WarehouseAvailability.Empty, result.Availability);
        Assert.Null(result.ExtractedAt);
    }

    [Fact]
    public async Task Reports_empty_when_the_fact_table_is_empty()
    {
        await postgres.ExecuteAsync("CREATE SCHEMA dw; CREATE TABLE dw.fact_case_event (extracted_at timestamptz NOT NULL)");

        var result = await _reader.GetLastExtractionAsync(CancellationToken.None);

        Assert.Equal(WarehouseAvailability.Empty, result.Availability);
    }

    [Fact]
    public async Task Reports_the_latest_extraction_when_the_fact_table_has_rows()
    {
        await postgres.ExecuteAsync(
            "CREATE SCHEMA dw; " +
            "CREATE TABLE dw.fact_case_event (extracted_at timestamptz NOT NULL); " +
            "INSERT INTO dw.fact_case_event VALUES ('2026-09-10T08:00:00Z'), ('2026-09-15T12:00:00Z')");

        var result = await _reader.GetLastExtractionAsync(CancellationToken.None);

        Assert.Equal(WarehouseAvailability.Ready, result.Availability);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), result.ExtractedAt);
    }
}
