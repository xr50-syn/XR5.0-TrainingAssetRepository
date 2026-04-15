using Microsoft.AspNetCore.Mvc;

namespace XR50TrainingAssetRepo.Infrastructure.ErrorHandling;

public static class ControllerProblemDetailsExtensions
{
    public static ActionResult ProblemBadRequest(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status400BadRequest, "Invalid request", detail,
            "https://api.xr50/errors/invalid-request", "invalid_request");

    public static ActionResult ProblemNotFound(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status404NotFound, "Resource not found", detail,
            "https://api.xr50/errors/resource-not-found", "resource_not_found");

    public static ActionResult ProblemConflict(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status409Conflict, "Conflict", detail,
            "https://api.xr50/errors/conflict", "conflict");

    public static ActionResult ProblemGone(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status410Gone, "Gone", detail,
            "https://api.xr50/errors/gone", "gone");

    public static ActionResult ProblemUnsupportedMediaType(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status415UnsupportedMediaType, "Unsupported media type", detail,
            "https://api.xr50/errors/unsupported-media-type", "unsupported_media_type");

    public static ActionResult ProblemServerError(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status500InternalServerError, "Internal server error", detail,
            "https://api.xr50/errors/internal-server-error", "internal_server_error");

    public static ActionResult ProblemBadGateway(this ControllerBase controller, string detail) =>
        controller.CreateProblem(StatusCodes.Status502BadGateway, "Upstream service failure", detail,
            "https://api.xr50/errors/upstream-service-failure", "upstream_service_failure");

    private static ObjectResult CreateProblem(
        this ControllerBase controller,
        int statusCode,
        string title,
        string detail,
        string type,
        string errorCode)
    {
        var problemDetails = ApiProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode,
            title,
            detail,
            type,
            errorCode);

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            DeclaredType = typeof(ProblemDetails),
            ContentTypes = { "application/problem+json" }
        };
    }
}
