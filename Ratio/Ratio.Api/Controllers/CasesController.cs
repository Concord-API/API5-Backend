using Microsoft.AspNetCore.Mvc;
using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Api.Controllers;

[ApiController]
[Route("api/cases/{number}")]
public class CasesController(ICaseDetailReader caseDetailReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string number, CancellationToken cancellationToken)
    {
        var digits = CaseNumbers.Digits(number);

        if (digits is null)
        {
            return Problem(
                detail: "Informe o número do processo no padrão CNJ, com 20 dígitos.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var detail = await caseDetailReader.GetAsync(digits, cancellationToken);

        return detail is null
            ? Problem(detail: "Processo não encontrado na amostra do Ratio.", statusCode: StatusCodes.Status404NotFound)
            : Ok(detail);
    }
}
