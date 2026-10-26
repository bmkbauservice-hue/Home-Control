using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HomeControl.Api.ErrorHandling;

// Fängt alle Exceptions ab, die in einem Endpunkt nicht behandelt wurden,
// und antwortet einheitlich mit ProblemDetails. Registriert in Program.cs.
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Fehler des Clients, z. B. kaputtes JSON im Body: bleibt 400, ist kein Serverfehler.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogInformation("Ungültige Anfrage bei {Method} {Path}: {Message}",
                httpContext.Request.Method, httpContext.Request.Path, badRequest.Message);

            return await WriteProblem(httpContext, new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "Ungültige Anfrage.",
                Detail = "Der Inhalt der Anfrage konnte nicht gelesen werden. Bitte gültiges JSON senden."
            });
        }

        // Alles andere ist unerwartet: vollständig ins Server-Log schreiben ...
        logger.LogError(exception, "Unerwarteter Fehler bei {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        // ... aber dem Client nur eine allgemeine Meldung geben, ohne Stacktrace oder interne Details.
        return await WriteProblem(httpContext, new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ein unerwarteter Fehler ist aufgetreten.",
            Detail = "Bitte versuche es später erneut."
        });
    }

    private async Task<bool> WriteProblem(HttpContext httpContext, ProblemDetails problem)
    {
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        });
    }
}
