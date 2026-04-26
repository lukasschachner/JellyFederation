using JellyFederation.Shared.Models;
using JellyFederation.Shared.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace JellyFederation.Server.Controllers;

/// <summary>
///     Base controller for endpoints protected by Federation auth.
///     Provides access to the authenticated server via CurrentServer.
/// </summary>
public abstract class AuthenticatedController : ControllerBase
{
    protected RegisteredServer CurrentServer =>
        HttpContext.Items.TryGetValue("Server", out var value) && value is RegisteredServer server
            ? server
            : throw new InvalidOperationException("Authenticated server context is not available.");

    protected string CorrelationId
    {
        get
        {
            if (HttpContext.Items.TryGetValue("CorrelationId", out var existing) &&
                existing is string current &&
                !string.IsNullOrWhiteSpace(current))
                return current;

            var correlationId = FederationTelemetry.CreateCorrelationId();
            HttpContext.Items["CorrelationId"] = correlationId;
            return correlationId;
        }
    }
}