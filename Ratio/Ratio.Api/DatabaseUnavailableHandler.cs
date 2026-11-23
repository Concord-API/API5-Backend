using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Ratio.Api;

public sealed class DatabaseUnavailableHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DatabaseUnavailableHandler> logger) : IExceptionHandler
{
    private const int RetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not NpgsqlException { IsTransient: true })
        {
            return false;
        }

        logger.LogWarning(exception, "O banco de dados não respondeu a tempo; a requisição recebeu 503.");

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "O banco de dados está ocupado. Tente de novo em alguns segundos."
            }
        });
    }
}
