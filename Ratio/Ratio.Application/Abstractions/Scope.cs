namespace Ratio.Application.Abstractions;

public sealed record Scope(IReadOnlyList<ScopeCourt> Courts, string Subject, string? Statement);

public sealed record ScopeCourt(string Code, string Name, string State);
