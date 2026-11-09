using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Ratio.Api.Tests.Observability;

public sealed class FileLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ratio-file-log-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Writes_the_trace_id_and_span_id_of_the_current_span_to_the_file_log()
    {
        string contentRoot;
        using (var factory = new ApiFactory())
        {
            contentRoot = factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(contentRoot, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Args:path"] = Path.Combine(_folder, "ratio-.log")
            })
            .Build();
        using var parent = new Activity("parent").Start();
        using var request = new Activity("request").Start();

        using (var logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger())
        {
            logger.Information("HTTP GET /api/themes responded 200");
        }

        var line = File.ReadAllLines(Assert.Single(Directory.GetFiles(_folder, "*.log")))
            .Single(entry => entry.Contains("/api/themes"));
        Assert.Contains($" {request.TraceId} {request.SpanId} ", line);
        Assert.DoesNotContain(parent.SpanId.ToString(), line);
    }
}
