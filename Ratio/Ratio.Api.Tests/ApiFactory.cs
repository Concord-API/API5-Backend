using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public Mock<ILastExtractionReader> LastExtractionReader { get; } = new();

    public Mock<IThemeSearchReader> ThemeSearchReader { get; } = new();

    public Mock<IDatabaseMigrator> DatabaseMigrator { get; } = new();

    public ApiFactory() =>
        DatabaseMigrator.Setup(migrator => migrator.MigrateAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Ratio", "Host=localhost;Database=unused");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(LastExtractionReader.Object);
            services.AddSingleton(ThemeSearchReader.Object);
            services.AddSingleton(DatabaseMigrator.Object);
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }
}
