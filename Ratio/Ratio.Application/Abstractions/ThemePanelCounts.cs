using Ratio.Application.Panels;

namespace Ratio.Application.Abstractions;

public sealed record JudgingBodyCount(string CourtCode, string? BodyName, bool IsPanel, long Upheld, long Rejected);

public sealed record ThemePanelCounts(long ThemeKey, IReadOnlyList<JudgingBodyCount> Bodies, PanelRule Rule);
