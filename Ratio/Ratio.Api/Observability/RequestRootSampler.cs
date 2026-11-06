using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Ratio.Api.Observability;

public sealed class RequestRootSampler : Sampler
{
    private static readonly SamplingResult Record = new(SamplingDecision.RecordAndSample);

    private static readonly SamplingResult Drop = new(SamplingDecision.Drop);

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.Kind == ActivityKind.Server ? Record : Drop;
}
