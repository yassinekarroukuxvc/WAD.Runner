using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using WAD.Runner.DataManagement.Domain.Validation;

namespace WAD.Runner.Api;

/// <summary>
/// Converts internal Runner exceptions into safe messages that may be returned
/// to WAD.Web and displayed to end users.
///
/// IMPORTANT:
/// - Do not return exception.ToString().
/// - Do not return stack traces, source paths, line numbers, SQL,
///   downstream API bodies, credentials, or other internal diagnostics.
/// - Full exception details belong in the Runner logs only.
/// </summary>
public static class JobErrorMapper
{
    public static PublicJobError Map(Exception exception, Guid jobId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var ex = Unwrap(exception);
        var reference = jobId.ToString("N");

        return ex switch
        {
            TaskCanceledException => new PublicJobError(
                Code: "TIMEOUT_OR_CANCELLED",
                StatusMessage: "Operation did not complete",
                Message:
                    "The automation could not complete because an operation was cancelled or timed out. " +
                    "Please try again. " +
                    $"Reference: {reference}"),

            OperationCanceledException => new PublicJobError(
                Code: "CANCELLED",
                StatusMessage: "Cancelled",
                Message: "The automation was cancelled."),

            WedgeTypeResolutionException => new PublicJobError(
                Code: "WEDGE_TYPE_ERROR",
                StatusMessage: "Wedge type could not be identified",
                Message:
                    "The wedge type could not be identified for the selected article. " +
                    "Please verify the wedge data and try again. " +
                    $"Reference: {reference}"),

            WedgeDimensionValidationException validationException => new PublicJobError(
                Code: "DIMENSION_VALIDATION_ERROR",
                StatusMessage: "Dimension validation failed",
                // This exception is an intentional business-validation exception.
                // Its Message is designed to tell the user which dimensions need correction.
                Message: validationException.Message),

            HttpRequestException => new PublicJobError(
                Code: "DATABASE_API_ERROR",
                StatusMessage: "Unable to load article data",
                Message:
                    "Unable to retrieve the required article data from the database. " +
                    "Please verify the article data and try again. " +
                    $"Reference: {reference}"),

            TimeoutException => new PublicJobError(
                Code: "TIMEOUT",
                StatusMessage: "Operation timed out",
                Message:
                    "The automation could not complete because an external operation timed out. " +
                    "Please try again. " +
                    $"Reference: {reference}"),

            COMException => new PublicJobError(
                Code: "SOLIDWORKS_ERROR",
                StatusMessage: "SolidWorks automation failed",
                Message:
                    "SolidWorks could not complete the requested automation. " +
                    "Please try again. If the problem continues, contact support. " +
                    $"Reference: {reference}"),

            IOException => new PublicJobError(
                Code: "FILE_ERROR",
                StatusMessage: "File operation failed",
                Message:
                    "The automation could not read, create, or save one of the required files. " +
                    "Please try again. If the problem continues, contact support. " +
                    $"Reference: {reference}"),

            UnauthorizedAccessException => new PublicJobError(
                Code: "FILE_ACCESS_ERROR",
                StatusMessage: "File access failed",
                Message:
                    "The automation does not have access to one of the required files or folders. " +
                    "Please contact support. " +
                    $"Reference: {reference}"),

            InvalidOperationException => new PublicJobError(
                Code: "AUTOMATION_DATA_ERROR",
                StatusMessage: "Automation could not process the wedge data",
                Message:
                    "The automation could not process the wedge data for the selected article. " +
                    "Please verify the article data and try again. " +
                    $"Reference: {reference}"),

            ArgumentException => new PublicJobError(
                Code: "INVALID_REQUEST_DATA",
                StatusMessage: "Invalid automation data",
                Message:
                    "The automation received invalid or unsupported data. " +
                    "Please verify the selected article and drawing options. " +
                    $"Reference: {reference}"),

            _ => new PublicJobError(
                Code: "UNEXPECTED_ERROR",
                StatusMessage: "Automation failed",
                Message:
                    "An unexpected error occurred while running the automation. " +
                    "Please try again. If the problem continues, contact support. " +
                    $"Reference: {reference}")
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;

        while (current is AggregateException aggregate &&
               aggregate.InnerExceptions.Count == 1 &&
               aggregate.InnerException is not null)
        {
            current = aggregate.InnerException;
        }

        return current;
    }
}

public sealed record PublicJobError(
    string Code,
    string StatusMessage,
    string Message);
