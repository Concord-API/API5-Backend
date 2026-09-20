using Microsoft.AspNetCore.Mvc;

namespace Ratio.Api.Tests;

/// <summary>
/// Endpoint que só existe nos testes: dá à suíte um parâmetro tipado para
/// provocar erro de model binding, que nenhuma rota de produção tem ainda.
/// </summary>
[ApiController]
[Route("test/validation-probe")]
public class ValidationProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] int limit) => Ok(new { limit });
}
