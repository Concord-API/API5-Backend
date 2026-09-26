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

    public Mock<IThemeDetailReader> ThemeDetailReader { get; } = new();

    public Mock<IProvenanceReader> ProvenanceReader { get; } = new();

    public Mock<IDoctrineReader> DoctrineReader { get; } = new();
    public Mock<IScopeReader> ScopeReader { get; } = new();

    public Mock<IDatabaseMigrator> DatabaseMigrator { get; } = new();

    public static LoadedProvenance LoadedCases { get; } = new(
        [new LoadedSource("cases", "datajud", new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), 12418)],
        "1.0");

    public static IReadOnlyList<ScopeCourt> Courts { get; } =
    [
        new("TJMG", "Tribunal de Justiça de Minas Gerais", "MG"),
        new("TJRJ", "Tribunal de Justiça do Rio de Janeiro", "RJ"),
        new("TJSP", "Tribunal de Justiça de São Paulo", "SP")
    ];

    public ApiFactory()
    {
        DatabaseMigrator.Setup(migrator => migrator.MigrateAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        ProvenanceReader.Setup(reader => reader.GetGlobalAsync(It.IsAny<CancellationToken>())).ReturnsAsync(LoadedProvenance.Empty);
        ProvenanceReader.Setup(reader => reader.GetThemeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(LoadedCases);
        DoctrineReader.Setup(reader => reader.GetRelatedAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        ScopeReader.Setup(reader => reader.GetCourtsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Courts);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Ratio", "Host=localhost;Database=unused");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(LastExtractionReader.Object);
            services.AddSingleton(ThemeSearchReader.Object);
            services.AddSingleton(ThemeDetailReader.Object);
            services.AddSingleton(ProvenanceReader.Object);
            services.AddSingleton(DoctrineReader.Object);
            services.AddSingleton(ScopeReader.Object);
            services.AddSingleton(DatabaseMigrator.Object);
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }
}
