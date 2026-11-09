using System.Diagnostics;
using OpenTelemetry.Trace;
using Ratio.Api.Observability;

namespace Ratio.Api.Tests.Observability;

public class RequestRootSamplerTests
{
    private static SamplingDecision DecideRoot(ActivityKind kind) =>
        new RequestRootSampler()
            .ShouldSample(new SamplingParameters(default, ActivityTraceId.CreateRandom(), "span", kind))
            .Decision;

    [Fact]
    public void Records_a_trace_started_by_a_request()
    {
        Assert.Equal(SamplingDecision.RecordAndSample, DecideRoot(ActivityKind.Server));
    }

    [Theory]
    [InlineData(ActivityKind.Client)]
    [InlineData(ActivityKind.Internal)]
    public void Drops_a_trace_that_no_request_started(ActivityKind kind)
    {
        Assert.Equal(SamplingDecision.Drop, DecideRoot(kind));
    }
}
