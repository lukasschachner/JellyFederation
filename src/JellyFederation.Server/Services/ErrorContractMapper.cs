using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace JellyFederation.Server.Services;

public sealed class ErrorContractMapper
{
    public static ObjectResult ToActionResult(FailureDescriptor failure) =>
        new(new ErrorEnvelope(ToContract(failure)))
        {
            StatusCode = ToStatusCode(failure.Category)
        };

    public static ErrorContract ToContract(FailureDescriptor failure)
    {
        var sanitized = TelemetryRedaction.SanitizeFailureDescriptor(failure);
        return new ErrorContract(
            sanitized.Code,
            sanitized.Category.ToString(),
            sanitized.Message,
            sanitized.CorrelationId,
            sanitized.Details);
    }

    // Example usage:
    // return _errorMapper.ToActionResult(FailureDescriptor.NotFound("invitation.not_found", "Invitation not found."));
    // return _errorMapper.ToActionResult(FailureDescriptor.Conflict("request.invalid_state", "Request is not in progress."));
    public static int ToStatusCode(FailureCategory category)
    {
        return category switch
        {
            FailureCategory.Validation => StatusCodes.Status400BadRequest,
            FailureCategory.Authorization => StatusCodes.Status403Forbidden,
            FailureCategory.NotFound => StatusCodes.Status404NotFound,
            FailureCategory.Conflict => StatusCodes.Status409Conflict,
            FailureCategory.Connectivity or FailureCategory.Timeout or FailureCategory.Reliability =>
                StatusCodes.Status503ServiceUnavailable,
            FailureCategory.Cancelled => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}