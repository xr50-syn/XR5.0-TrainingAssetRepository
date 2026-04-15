using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Infrastructure.ErrorHandling;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var mapping = MapException(exception, environment.IsDevelopment());

        httpContext.Response.StatusCode = mapping.StatusCode;

        var problemDetails = ApiProblemDetailsFactory.CreateProblemDetails(
            httpContext,
            mapping.StatusCode,
            mapping.Title,
            mapping.Detail,
            mapping.Type,
            mapping.ErrorCode);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    private static ExceptionMapping MapException(Exception exception, bool includeExceptionDetails)
    {
        return exception switch
        {
            KeyNotFoundException => new ExceptionMapping(
                StatusCodes.Status404NotFound,
                "Resource not found",
                exception.Message,
                "https://api.xr50/errors/resource-not-found",
                "resource_not_found"),

            ArgumentException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message,
                "https://api.xr50/errors/invalid-request",
                "invalid_request"),

            JsonException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Malformed JSON",
                "The request body contains invalid JSON.",
                "https://api.xr50/errors/malformed-json",
                "malformed_json"),

            DbUpdateConcurrencyException => new ExceptionMapping(
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                "The resource was modified by another request. Retry the operation.",
                "https://api.xr50/errors/concurrency-conflict",
                "concurrency_conflict"),

            NotSupportedException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Operation not supported",
                exception.Message,
                "https://api.xr50/errors/operation-not-supported",
                "operation_not_supported"),

            ChatbotApiException => new ExceptionMapping(
                StatusCodes.Status502BadGateway,
                "Upstream service failure",
                includeExceptionDetails
                    ? exception.Message
                    : "A dependent service failed while processing the request.",
                "https://api.xr50/errors/upstream-service-failure",
                "upstream_service_failure"),

            HttpRequestException => new ExceptionMapping(
                StatusCodes.Status502BadGateway,
                "Upstream service failure",
                includeExceptionDetails
                    ? exception.Message
                    : "A dependent service failed while processing the request.",
                "https://api.xr50/errors/upstream-service-failure",
                "upstream_service_failure"),

            _ => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                includeExceptionDetails
                    ? exception.Message
                    : "An unexpected error occurred while processing the request.",
                "https://api.xr50/errors/internal-server-error",
                "internal_server_error")
        };
    }

    private sealed record ExceptionMapping(
        int StatusCode,
        string Title,
        string Detail,
        string Type,
        string ErrorCode);
}

public static class ApiProblemDetailsFactory
{
    public static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string type,
        string errorCode)
    {
        return new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = type,
            Instance = httpContext.Request.Path
        }.WithStandardExtensions(httpContext, errorCode);
    }

    public static ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext,
        ModelStateDictionary modelState)
    {
        return new ValidationProblemDetails(modelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = "One or more validation errors occurred.",
            Type = "https://api.xr50/errors/validation-failed",
            Instance = httpContext.Request.Path
        }.WithStandardExtensions(httpContext, "validation_failed");
    }

    public static ProblemDetails CreateStatusCodeProblemDetails(HttpContext httpContext, int statusCode)
    {
        var (title, type, errorCode) = statusCode switch
        {
            StatusCodes.Status401Unauthorized => (
                "Unauthorized",
                "https://api.xr50/errors/unauthorized",
                "unauthorized"),

            StatusCodes.Status403Forbidden => (
                "Forbidden",
                "https://api.xr50/errors/forbidden",
                "forbidden"),

            StatusCodes.Status404NotFound => (
                "Resource not found",
                "https://api.xr50/errors/resource-not-found",
                "resource_not_found"),

            StatusCodes.Status405MethodNotAllowed => (
                "Method not allowed",
                "https://api.xr50/errors/method-not-allowed",
                "method_not_allowed"),

            _ => (
                "Request failed",
                "https://api.xr50/errors/request-failed",
                "request_failed")
        };

        var detail = statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Authentication is required to access this resource.",
            StatusCodes.Status403Forbidden => "You do not have permission to access this resource.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status405MethodNotAllowed => "The requested HTTP method is not allowed for this resource.",
            _ => "The request could not be completed."
        };

        return CreateProblemDetails(httpContext, statusCode, title, detail, type, errorCode);
    }

    private static T WithStandardExtensions<T>(this T problemDetails, HttpContext httpContext, string errorCode)
        where T : ProblemDetails
    {
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["errorCode"] = errorCode;
        return problemDetails;
    }
}
