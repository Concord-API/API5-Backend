using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Ratio.Api.Observability;

public static class ObservabilityExtensions
{
    private const string ServiceName = "ratio-api";

    public static Uri? ReadOtlpEndpoint(this IConfiguration configuration)
    {
        var value = configuration["OpenTelemetry:Endpoint"];
        return string.IsNullOrWhiteSpace(value) ? null : new Uri(value, UriKind.Absolute);
    }

    public static IServiceCollection AddRatioObservability(this IServiceCollection services, Uri? otlpEndpoint)
    {
        if (otlpEndpoint is null)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint));
        return services;
    }
}
