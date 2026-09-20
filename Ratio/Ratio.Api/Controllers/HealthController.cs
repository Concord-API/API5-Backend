using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController(ILastExtractionReader lastExtractionReader) : ControllerBase
{
    [HttpGet]
    public ContentResult Live()
    {
        return Content("Saudável", "text/plain; charset=utf-8");
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var lastExtraction = await lastExtractionReader.GetLastExtractionAsync(cancellationToken);

        return lastExtraction.Availability switch
        {
            WarehouseAvailability.Ready =>
                Ok(new { status = "Pronto", lastExtractionAt = lastExtraction.ExtractedAt }),

            WarehouseAvailability.Empty => NotReady(
                reason: "sem carga publicada",
                detail: "O banco respondeu, mas não há carga publicada no schema dw. "
                      + "Restaure o dump da última carga."),

            _ => NotReady(
                reason: "banco inacessível",
                detail: "Não foi possível consultar o banco de dados. "
                      + "A causa está no log da aplicação.")
        };
    }

    /// <summary>
    /// 503 com o motivo legível em <c>detail</c> e em <c>reason</c>, para que o
    /// monitoramento distinga falta de carga de banco fora do ar sem ler prosa.
    /// </summary>
    private ObjectResult NotReady(string reason, string detail)
    {
        var response = Problem(
            title: "Não está pronto",
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable);

        ((ProblemDetails)response.Value!).Extensions["reason"] = reason;
        return response;
    }
}
