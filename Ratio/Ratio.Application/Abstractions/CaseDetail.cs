namespace Ratio.Application.Abstractions;

public enum CaseAvailability
{
    Available,
    Sealed,
    SourceUnavailable,
    NotFoundOnSite,
    NotLoaded
}

public sealed record CaseFields(
    string? CaseClass,
    IReadOnlyList<string> Subjects,
    string? JudgingBody,
    string? Judge,
    DateOnly? FiledAt,
    DateOnly? DecisionDate,
    string? DecisionResult,
    string? Situation);

public sealed record CaseMovement(DateOnly Date, string Description);

public sealed record CaseSource(string Name, string Url, DateTimeOffset ExtractedAt);

public sealed record CaseDetail(
    string? CaseNumber,
    string? Court,
    CaseAvailability Status,
    string? Message,
    CaseFields? Fields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<CaseMovement> Movements,
    CaseSource? Source);

public sealed record CaseSiteRecord(
    string CaseNumber,
    string Court,
    bool Sealed,
    string? SiteStatus,
    CaseFields? Fields,
    IReadOnlyList<CaseMovement> Movements,
    string? SourceUrl,
    DateTimeOffset? ExtractedAt);

public interface ICaseDetailReader
{
    Task<CaseDetail?> GetAsync(string caseNumberDigits, CancellationToken cancellationToken);
}
