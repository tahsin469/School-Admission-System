using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Exceptions;

namespace SchoolAdmission.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate _next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        this._next = _next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Validation Failed";
                problemDetails.Detail = "One or more validation errors occurred.";
                problemDetails.Extensions["errors"] = validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );
                break;

            case NotFoundException notFoundEx:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Resource Not Found";
                problemDetails.Detail = notFoundEx.Message;
                break;

            case SeatConflictException conflictEx:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Seat Conflict";
                problemDetails.Detail = conflictEx.Message;
                break;

            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                // High-concurrency race condition: Row modified/locked by another thread during SaveChanges
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Seat Conflict";
                problemDetails.Detail = "The requested seat was allocated by another concurrent request. Please try again.";
                break;

            case TimeoutException or System.IO.IOException or System.Net.Sockets.SocketException:
                // Extreme Surge: Connection pool / network saturation under 100,000+ load
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                problemDetails.Status = (int)HttpStatusCode.ServiceUnavailable;
                problemDetails.Title = "Service Temporarily Busy";
                problemDetails.Detail = "System is experiencing heavy admission traffic surge. Please retry in a few moments.";
                break;

            case BusinessRuleException businessEx:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                problemDetails.Status = (int)HttpStatusCode.UnprocessableEntity;
                problemDetails.Title = "Business Rule Violation";
                problemDetails.Detail = businessEx.Message;
                break;

            case OperationCanceledException:
                // Client aborted the request (e.g., closed tab or short polling timeout)
                context.Response.StatusCode = 499; // Client Closed Request
                problemDetails.Status = 499;
                problemDetails.Title = "Client Closed Request";
                problemDetails.Detail = "The operation was canceled by the client.";
                break;

            case ArgumentException argEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Bad Request";
                problemDetails.Detail = argEx.Message;
                break;

            default:
                // Fallback for unexpected system errors: NEVER leak stack trace
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Internal Server Error";
                problemDetails.Detail = "An unexpected error occurred. Please try again later.";
                break;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var responseJson = JsonSerializer.Serialize(problemDetails, jsonOptions);
        await context.Response.WriteAsync(responseJson);
    }
}
