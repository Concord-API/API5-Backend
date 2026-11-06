using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Ratio.Api.Tests.Observability;

public sealed record ExportedLog(
    string Category, LogLevel Level, string? Message, IReadOnlyDictionary<string, object?> Attributes,
    ActivityTraceId TraceId, ActivitySpanId SpanId);

public sealed class CapturingLogProcessor : BaseProcessor<LogRecord>
{
    public ConcurrentQueue<ExportedLog> Logs { get; } = new();

    public override void OnEnd(LogRecord data) =>
        Logs.Enqueue(new ExportedLog(
            data.CategoryName ?? "",
            data.LogLevel,
            data.FormattedMessage,
            (data.Attributes ?? []).ToDictionary(attribute => attribute.Key, attribute => attribute.Value),
            data.TraceId,
            data.SpanId));
}
