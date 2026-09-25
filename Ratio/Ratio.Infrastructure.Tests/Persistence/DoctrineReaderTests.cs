using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class DoctrineReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Model = "paraphrase-multilingual-MiniLM-L12-v2";

    private const string Themes =
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Dano moral', 412), ('Cobrança de dívida', 500); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Indenização por dano moral', 1001), ('Dano moral coletivo', 1002), ('Cobrança', 2001); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s " +
        "ON (t.theme_key = 412 AND s.subject_code IN (1001, 1002)) OR (t.theme_key = 500 AND s.subject_code = 2001);";

    private NpgsqlDataSource _dataSource = null!;
    private DoctrineReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new DoctrineReader(_dataSource);
        await ExecuteAsync(Themes);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task AddArticleAsync(string title, string? doi, string? url, int subjectCode, double? similarity) =>
        ExecuteAsync(
            "INSERT INTO dw.dim_doctrine (title, authors, journal_name, publication_year, doi, article_url, source, extracted_at) " +
            $"VALUES ('{title}', 'Maria Silva', 'Revista da EMERJ', 2021, {Literal(doi)}, {Literal(url)}, 'oai_emerj', now()) " +
            "ON CONFLICT DO NOTHING; " +
            "INSERT INTO dw.bridge_subject_doctrine (subject_sk, doctrine_sk, link_method, similarity, embedding_model) " +
            $"SELECT s.subject_sk, d.doctrine_sk, 'embedding_cosine+lexical', {Literal(similarity)}, '{Model}' " +
            $"FROM dw.dim_subject s CROSS JOIN dw.dim_doctrine d WHERE s.subject_code = {subjectCode} AND d.title = '{title}';");

    private static string Literal(string? value) => value is null ? "NULL" : $"'{value}'";

    private static string Literal(double? value) =>
        value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task Reads_the_related_doctrine_by_descending_similarity()
    {
        await AddArticleAsync("Artigo médio", "10.1/b", null, 1001, 0.70);
        await AddArticleAsync("Artigo forte", "10.1/a", null, 1001, 0.90);
        await AddArticleAsync("Artigo fraco", "10.1/c", null, 1001, 0.60);

        var entries = await _reader.GetRelatedAsync(412, 20, CancellationToken.None);

        Assert.Equal(["Artigo forte", "Artigo médio", "Artigo fraco"], entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task Reads_every_field_of_an_entry()
    {
        await AddArticleAsync("Dano moral: aspectos históricos", "10.1234/emerj.2021.001", "https://emerj.example/1", 1001, 0.854);

        var entry = Assert.Single(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));

        Assert.Equal("Dano moral: aspectos históricos", entry.Title);
        Assert.Equal("Maria Silva", entry.Authors);
        Assert.Equal("Revista da EMERJ", entry.Journal);
        Assert.Equal(2021, entry.PublicationYear);
        Assert.Equal("10.1234/emerj.2021.001", entry.Doi);
        Assert.Equal("oai_emerj", entry.Source);
        Assert.Equal(0.854, entry.Similarity!.Value, 3);
        Assert.Equal("embedding_cosine+lexical", entry.LinkMethod);
        Assert.Equal(Model, entry.EmbeddingModel);
    }

    [Fact]
    public async Task Links_to_the_doi_when_the_article_has_one()
    {
        await AddArticleAsync("Com DOI", "10.1234/x", "https://emerj.example/x", 1001, 0.80);

        var entry = Assert.Single(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));

        Assert.Equal("https://doi.org/10.1234/x", entry.Link);
    }

    [Fact]
    public async Task Links_to_the_article_address_when_there_is_no_doi()
    {
        await AddArticleAsync("Sem DOI", null, "https://emerj.example/y", 1001, 0.80);

        var entry = Assert.Single(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));

        Assert.Null(entry.Doi);
        Assert.Equal("https://emerj.example/y", entry.Link);
    }

    [Fact]
    public async Task Lists_an_article_linked_to_two_subjects_once_with_the_highest_similarity()
    {
        await AddArticleAsync("Artigo compartilhado", "10.1/shared", null, 1001, 0.65);
        await AddArticleAsync("Artigo compartilhado", "10.1/shared", null, 1002, 0.88);

        var entry = Assert.Single(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));

        Assert.Equal(0.88, entry.Similarity!.Value, 3);
    }

    [Fact]
    public async Task Lists_a_link_without_similarity_last()
    {
        await AddArticleAsync("Sem similaridade", "10.1/null", null, 1001, null);
        await AddArticleAsync("Com similaridade", "10.1/sim", null, 1001, 0.60);

        var entries = await _reader.GetRelatedAsync(412, 20, CancellationToken.None);

        Assert.Equal(["Com similaridade", "Sem similaridade"], entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task Leaves_out_the_doctrine_of_another_theme()
    {
        await AddArticleAsync("Artigo do tema", "10.1/own", null, 1001, 0.80);
        await AddArticleAsync("Artigo de cobrança", "10.1/other", null, 2001, 0.95);

        var entry = Assert.Single(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));

        Assert.Equal("Artigo do tema", entry.Title);
    }

    [Fact]
    public async Task Returns_at_most_the_requested_number_of_entries()
    {
        await AddArticleAsync("Primeiro", "10.1/1", null, 1001, 0.90);
        await AddArticleAsync("Segundo", "10.1/2", null, 1001, 0.80);
        await AddArticleAsync("Terceiro", "10.1/3", null, 1001, 0.70);

        var entries = await _reader.GetRelatedAsync(412, 2, CancellationToken.None);

        Assert.Equal(["Primeiro", "Segundo"], entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task Returns_an_empty_list_for_a_theme_without_doctrine()
    {
        Assert.Empty(await _reader.GetRelatedAsync(412, 20, CancellationToken.None));
    }
}
