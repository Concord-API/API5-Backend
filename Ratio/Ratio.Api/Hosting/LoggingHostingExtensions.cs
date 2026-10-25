using Serilog;

namespace Ratio.Api.Hosting;

public static class LoggingHostingExtensions
{
    public static WebApplicationBuilder UseRatioLogging(this WebApplicationBuilder builder, Action<IHostBuilder> runAsService)
    {
        runAsService(builder.Host);
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext(), writeToProviders: true);
        return builder;
    }
}
