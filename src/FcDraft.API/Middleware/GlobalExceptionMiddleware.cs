using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IErrorReporter errorReporter,
    ICorrelationIdAccessor correlation)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                logger.LogDebug(exception, "Request ended after the response started");
                return;
            }

            var (status, title) = exception switch
            {
                ValidationAppException => (StatusCodes.Status400BadRequest, "Validation failed"),
                UnauthorizedAppException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                ForbiddenAppException => (StatusCodes.Status403Forbidden, "Forbidden"),
                ConflictAppException => (StatusCodes.Status409Conflict, "Conflict"),
                TooManyRequestsAppException => (StatusCodes.Status429TooManyRequests, "Too many requests"),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
            };

            if (status >= 500)
            {
                // The error-monitoring seam (PR-22) owns logging and counting unexpected errors;
                // expected 4xx app exceptions are business outcomes and are not reported.
                errorReporter.Report(exception, correlation.CorrelationId, context.Request.Path);
            }

            var details = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status < 500 ? exception.Message : "Please try again."
            };
            // Lets a user or support ticket quote the id that finds the matching server logs.
            details.Extensions["correlationId"] = correlation.CorrelationId;
            if (exception is ValidationAppException validation)
            {
                details.Extensions["errors"] = validation.Errors;
            }

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(details);
        }
    }
}
