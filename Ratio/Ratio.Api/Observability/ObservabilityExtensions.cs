using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Ratio.Api.Observability;

public static class ObservabilityExtensions
{
    private const string ServiceName = "ratio-api";

    private static readonly string[] Meters =
    [
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "System.Runtime"
    ];

    public static Uri? ReadOtlpEndpoint(this IConfiguration configuration)
    {
        var value = configuration["OpenTelemetry:Endpoint"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException(
                $"OpenTelemetry endpoint '{value}' is not an absolute URI. Set OpenTelemetry__Endpoint.");
    }

    public static IServiceCollection AddRatioObservability(this IServiceCollection services, Uri? otlpEndpoint)
    {
        if (otlpEndpoint is null)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(Meters)
                .AddOtlpExporter(options => options.Endpoint = otlpEndpoint));
        return services;
    }
}
