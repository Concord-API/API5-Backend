using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class DoctrineReader(NpgsqlDataSource dataSource) : IDoctrineReader
{
    private const string Sql = """
        SELECT d.title AS Title, d.authors AS Authors, d.journal_name AS Journal,
               d.publication_year AS PublicationYear, d.doi AS Doi,
               COALESCE('https://doi.org/' || d.doi, d.article_url) AS Link, d.source AS Source,
               l.similarity::float8 AS Similarity, l.link_method AS LinkMethod, l.embedding_model AS EmbeddingModel
        FROM (
            SELECT DISTINCT ON (bsd.doctrine_sk) bsd.doctrine_sk, bsd.similarity, bsd.link_method, bsd.embedding_model
            FROM dw.dim_theme t
            JOIN dw.bridge_theme_subject bts ON bts.theme_sk = t.theme_sk
            JOIN dw.bridge_subject_doctrine bsd ON bsd.subject_sk = bts.subject_sk
            WHERE t.theme_key = @themeKey
            ORDER BY bsd.doctrine_sk, bsd.similarity DESC NULLS LAST
        ) l
        JOIN dw.dim_doctrine d ON d.doctrine_sk = l.doctrine_sk
        ORDER BY l.similarity DESC NULLS LAST, d.title
        LIMIT @limit
        """;

    public async Task<IReadOnlyList<DoctrineEntry>> GetRelatedAsync(long themeKey, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DoctrineRow>(
            new CommandDefinition(Sql, new { themeKey, limit }, cancellationToken: cancellationToken));

        return rows.Select(ToEntry).ToArray();
    }

    private static DoctrineEntry ToEntry(DoctrineRow row) =>
        new(
            row.Title,
            row.Authors,
            row.Journal,
            row.PublicationYear,
            row.Doi,
            row.Link,
            row.Source,
            row.Similarity,
            row.LinkMethod,
            row.EmbeddingModel);

    private sealed record DoctrineRow(
        string Title,
        string? Authors,
        string? Journal,
        short? PublicationYear,
        string? Doi,
        string? Link,
        string Source,
        double? Similarity,
        string LinkMethod,
        string? EmbeddingModel);
}
