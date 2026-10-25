using Serilog;

namespace Ratio.Api.Hosting;

public static class LoggingHostingExtensions
{
    public static WebApplicationBuilder UseRatioLogging(this WebApplicationBuilder builder, Action<IHostBuilder> runAsService)
    {
        builder.Logging.ClearProviders();
        runAsService(builder.Host);
        builder.Host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext(), writeToProviders: true);
        return builder;
    }
}
