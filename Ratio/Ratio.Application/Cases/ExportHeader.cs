using Ratio.Application.Abstractions;

namespace Ratio.Application.Cases;

public sealed record ExportHeader(IReadOnlyList<ProvenanceSource> Sources, Scope Scope);
