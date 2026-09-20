using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Ratio.Api;

public static class ProblemDetailsTitles
{
    private const string FrameworkUnhandledErrorTitle = "An error occurred while processing your request.";

    public static void Localize(ProblemDetails problem)
    {
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (problem.Title is null
            || problem.Title == ReasonPhrases.GetReasonPhrase(status)
            || problem.Title == FrameworkUnhandledErrorTitle)
        {
            problem.Title = For(status);
        }
    }

    public static string For(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Requisição inválida",
        StatusCodes.Status404NotFound => "Recurso não encontrado",
        StatusCodes.Status405MethodNotAllowed => "Método não permitido",
        StatusCodes.Status500InternalServerError => "Erro interno do servidor",
        StatusCodes.Status503ServiceUnavailable => "Serviço indisponível",
        _ => "Não foi possível concluir a requisição"
    };
}
