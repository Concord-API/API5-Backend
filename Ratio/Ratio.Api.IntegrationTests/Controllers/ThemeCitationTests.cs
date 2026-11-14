using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Ratio.Api.IntegrationTests.Controllers;

public class ThemeCitationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly char[] Markup = ['<', '>', '*', '_', '#', '`', '[', ']', '|', '\r', '\n', '\t'];

    private string _connectionString = null!;
    private RatioApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _api = new RatioApiFactory(_connectionString);
        _client = _api.CreateClient();
        await _api.LoadSampleAsync();
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private async Task<JsonElement> GetThemeAsync(long key) =>
        await _client.GetFromJsonAsync<JsonElement>($"/api/themes/{key}");

    [Fact]
    public async Task Cites_the_loaded_theme_with_its_number_n_source_date_and_scope()
    {
        var theme = await GetThemeAsync(1);

        Assert.Equal(
            "Cobrança de dívida — Nas decisões julgadas, houve acolhimento da pretensão do autor. "
            + "Fonte: DataJud/CNJ, 40 processos, extração de 01.10.2026. "
            + "Escopo: TJMG, TJRJ e TJSP, matéria cível. Metodologia 1.0.",
            theme.GetProperty("citation").GetString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Keeps_markup_and_forced_line_breaks_out_of_the_citation(long key)
    {
        var citation = (await GetThemeAsync(key)).GetProperty("citation").GetString();

        Assert.NotNull(citation);
        Assert.Equal(-1, citation.IndexOfAny(Markup));
        Assert.Equal(citation.Trim(), citation);
        Assert.DoesNotContain("  ", citation);
    }

    [Fact]
    public async Task Leaves_the_unavailable_blocks_out_of_the_citation()
    {
        var theme = await GetThemeAsync(1);
        var citation = theme.GetProperty("citation").GetString()!;

        var unavailable = theme.GetProperty("unavailable").EnumerateArray().ToArray();

        Assert.NotEmpty(unavailable);
        Assert.All(unavailable, block =>
            Assert.DoesNotContain(block.GetProperty("message").GetString()!, citation));
        Assert.DoesNotContain("relator", citation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inteiro teor", citation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gives_no_citation_to_a_theme_without_sourced_cases()
    {
        await using (var dataSource = NpgsqlDataSource.Create(_connectionString))
        await using (var command = dataSource.CreateCommand(
            """
            INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema sem processos', 4);
            REFRESH MATERIALIZED VIEW dw.theme_provenance;
            """))
        {
            await command.ExecuteNonQueryAsync();
        }

        var theme = await GetThemeAsync(4);

        Assert.Equal("Tema sem processos", theme.GetProperty("name").GetString());
        Assert.Empty(theme.GetProperty("provenance").GetProperty("sources").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, theme.GetProperty("citation").ValueKind);
    }
}
