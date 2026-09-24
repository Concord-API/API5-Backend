using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Hosting.WindowsServices;
using Ratio.Api;
using Ratio.Api.Hosting;
using Ratio.Infrastructure;
using Serilog;

if (WindowsServiceHelpers.IsWindowsService())
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}

var builder = WebApplication.CreateBuilder(args);

string connectionString;
string[] allowedOrigins;
using (var startupLogger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger())
{
    try
    {
        connectionString = builder.Configuration.GetConnectionString("Ratio")
            ?? throw new InvalidOperationException("Connection string 'Ratio' is not configured. Set ConnectionStrings__Ratio.");
        allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    }
    catch (Exception exception)
    {
        startupLogger.Fatal(exception, "A API não conseguiu subir: configuração inválida.");
        throw;
    }
}

builder.Host.UseWindowsService();
builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddControllers(options =>
{
    var messages = options.ModelBindingMessageProvider;
    messages.SetValueIsInvalidAccessor(value => $"O valor {value} não é válido.");
    messages.SetAttemptedValueIsInvalidAccessor((value, _) => $"O valor '{value}' não é válido.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"O valor '{value}' não é válido.");
    messages.SetMissingRequestBodyRequiredValueAccessor(() => "O corpo da requisição é obrigatório.");
    messages.SetValueMustNotBeNullAccessor(_ => "O campo é obrigatório.");
    messages.SetMissingBindRequiredValueAccessor(field => $"O campo '{field}' é obrigatório.");
    messages.SetMissingKeyOrValueAccessor(() => "O campo é obrigatório.");
    messages.SetUnknownValueIsInvalidAccessor(field => $"O valor informado em '{field}' não é válido.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "O valor informado não é válido.");
    messages.SetValueMustBeANumberAccessor(field => $"O campo '{field}' precisa ser um número.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "O valor precisa ser um número.");
}).AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(
            context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest);
        ProblemDetailsTitles.Localize(problem);

        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" }
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context => ProblemDetailsTitles.Localize(context.ProblemDetails));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET")));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapControllers();

app.Run();

public partial class Program;
