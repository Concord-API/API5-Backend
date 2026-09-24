using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests;

[ApiController]
[Route("test/unavailable-probe")]
public class UnavailableProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        unavailable = new[]
        {
            new UnavailableBlock("reporterJudge", UnavailableReason.SourceUnavailable, "O DataJud não publica o relator."),
            new UnavailableBlock("summary", UnavailableReason.NotLoaded, "O texto deste tema ainda não foi gerado; ele sai na próxima carga."),
            new UnavailableBlock("summary", UnavailableReason.NotApplicable, "Este tema não tem decisões julgadas, então não há entendimento para descrever.")
        }
    });
}
