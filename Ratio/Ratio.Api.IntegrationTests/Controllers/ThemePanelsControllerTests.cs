using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Ratio.Api.IntegrationTests.Controllers;

public class ThemePanelsControllerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string AppealsOfTheDebtCollectionTheme = """
        CREATE TEMPORARY TABLE appeal (case_number text, body_name text, upheld boolean);

        INSERT INTO appeal (case_number, body_name, upheld)
        SELECT 'APPEAL-' || b.label || '-' || n, b.body_name, n <= b.upheld
        FROM (VALUES
            ('A', '3ª Câmara de Direito Privado', 6, 6),
            ('B', '5ª Câmara de Direito Privado', 6, 0),
            ('C', '8ª Câmara de Direito Privado', 6, 3),
            ('D', '12ª Câmara de Direito Privado', 4, 4),
            ('E', '20ª Câmara de Direito Privado', 1, 1),
            ('F', '1ª Vara Cível', 2, 0),
            ('G', NULL, 1, 0)
        ) AS b (label, body_name, total, upheld)
        CROSS JOIN LATERAL generate_series(1, b.total) AS n;

        INSERT INTO dw.dim_judging_body (court_sk, body_name)
        SELECT DISTINCT c.court_sk, a.body_name
        FROM appeal a
        CROSS JOIN dw.dim_court c
        WHERE c.court_code = 'TJSP' AND a.body_name IS NOT NULL;

        INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at)
        SELECT a.case_number, c.court_sk, 'Second', 'datajud', TIMESTAMPTZ '2026-10-01 12:00:00+00'
        FROM appeal a
        CROSS JOIN dw.dim_court c
        WHERE c.court_code = 'TJSP';

        INSERT INTO dw.bridge_case_subject (case_sk, subject_sk)
        SELECT dc.case_sk, s.subject_sk
        FROM appeal a
        JOIN dw.dim_case dc ON dc.case_number = a.case_number
        JOIN dw.dim_subject s ON s.subject_code = 100;

        INSERT INTO dw.fact_case_event
            (case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key)
        SELECT dc.case_sk, dc.court_sk, jb.judging_body_sk, m.movement_sk, 20250902, TIMESTAMPTZ '2025-09-02',
               'https://example.org/processo/' || dc.case_number, TIMESTAMPTZ '2026-10-01 12:00:00+00',
               'appeal:' || dc.case_number
        FROM appeal a
        JOIN dw.dim_case dc ON dc.case_number = a.case_number
        LEFT JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk AND jb.body_name = a.body_name
        JOIN dw.dim_movement m ON m.movement_code = CASE WHEN a.upheld THEN 237 ELSE 239 END;

        DROP TABLE appeal;

        REFRESH MATERIALIZED VIEW dw.case_current_result;
        REFRESH MATERIALIZED VIEW dw.theme_by_judging_body;
        """;

    private RatioApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        _api = new RatioApiFactory(database);
        _client = _api.CreateClient();
        await _api.LoadSampleAsync();
        await using var dataSource = NpgsqlDataSource.Create(database);
        await using var command = dataSource.CreateCommand(AppealsOfTheDebtCollectionTheme);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private static JsonElement Panel(JsonElement court, string name) =>
        court.GetProperty("panels").EnumerateArray().Single(panel => panel.GetProperty("name").GetString() == name);

    [Fact]
    public async Task Compares_each_panel_with_the_appeals_of_its_court()
    {
        var response = await _client.GetAsync("/api/themes/1/panels");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("themeKey").GetInt64());
        Assert.Equal("provimento do recurso (pretensão de quem recorreu)", body.GetProperty("basis").GetString());
        var court = Assert.Single(body.GetProperty("courts").EnumerateArray());
        Assert.Equal("TJSP", court.GetProperty("courtCode").GetString());
        Assert.Equal(26, court.GetProperty("n").GetInt64());
        Assert.Equal(0.5385m, court.GetProperty("percentage").GetDecimal());
        var panel = Panel(court, "3ª Câmara de Direito Privado");
        Assert.Equal(6, panel.GetProperty("n").GetInt64());
        Assert.Equal(1m, panel.GetProperty("percentage").GetDecimal());
        Assert.Equal(46.2m, panel.GetProperty("distanceInPoints").GetDecimal());
    }

    [Fact]
    public async Task Flags_only_the_panels_twenty_points_away_from_five_judged_appeals_up()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/1/panels");

        var rule = body.GetProperty("rule");
        Assert.Equal(20, rule.GetProperty("divergencePoints").GetInt32());
        Assert.Equal(5, rule.GetProperty("minJudgedForDivergence").GetInt32());
        var court = Assert.Single(body.GetProperty("courts").EnumerateArray());
        Assert.True(Panel(court, "3ª Câmara de Direito Privado").GetProperty("divergent").GetBoolean());
        Assert.Equal(-53.8m, Panel(court, "5ª Câmara de Direito Privado").GetProperty("distanceInPoints").GetDecimal());
        Assert.True(Panel(court, "5ª Câmara de Direito Privado").GetProperty("divergent").GetBoolean());
        Assert.Equal(-3.8m, Panel(court, "8ª Câmara de Direito Privado").GetProperty("distanceInPoints").GetDecimal());
        Assert.False(Panel(court, "8ª Câmara de Direito Privado").GetProperty("divergent").GetBoolean());
        Assert.Equal(46.2m, Panel(court, "12ª Câmara de Direito Privado").GetProperty("distanceInPoints").GetDecimal());
        Assert.False(Panel(court, "12ª Câmara de Direito Privado").GetProperty("divergent").GetBoolean());
    }

    [Fact]
    public async Task Leaves_a_single_judgment_and_the_bodies_that_are_not_panels_outside_the_panels()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/1/panels");

        var court = Assert.Single(body.GetProperty("courts").EnumerateArray());
        var names = court.GetProperty("panels").EnumerateArray().Select(panel => panel.GetProperty("name").GetString());
        Assert.Equal(
            new string?[]
            {
                "3ª Câmara de Direito Privado",
                "5ª Câmara de Direito Privado",
                "8ª Câmara de Direito Privado",
                "12ª Câmara de Direito Privado"
            },
            names);
        Assert.Equal(4, court.GetProperty("outsidePanels").GetInt64());
    }
}
