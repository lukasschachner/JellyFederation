using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class FailureDescriptorFactoryTests
{
    [Theory]
    [InlineData("Validation", "code.validation", "msg", "corr")]
    [InlineData("Authorization", "code.auth", "msg", "corr")]
    [InlineData("NotFound", "code.notfound", "msg", "corr")]
    [InlineData("Conflict", "code.conflict", "msg", "corr")]
    [InlineData("Connectivity", "code.connectivity", "msg", "corr")]
    [InlineData("Timeout", "code.timeout", "msg", "corr")]
    [InlineData("Cancelled", "code.cancelled", "msg", "corr")]
    [InlineData("Reliability", "code.reliability", "msg", "corr")]
    [InlineData("Unexpected", "code.unexpected", "msg", "corr")]
    public void StaticFactories_CreateExpectedCategory(string factoryName, string code, string message, string correlationId)
    {
        var failure = factoryName switch
        {
            "Validation" => FailureDescriptor.Validation(code, message, correlationId),
            "Authorization" => FailureDescriptor.Authorization(code, message, correlationId),
            "NotFound" => FailureDescriptor.NotFound(code, message, correlationId),
            "Conflict" => FailureDescriptor.Conflict(code, message, correlationId),
            "Connectivity" => FailureDescriptor.Connectivity(code, message, correlationId),
            "Timeout" => FailureDescriptor.Timeout(code, message, correlationId),
            "Cancelled" => FailureDescriptor.Cancelled(code, message, correlationId),
            "Reliability" => FailureDescriptor.Reliability(code, message, correlationId),
            "Unexpected" => FailureDescriptor.Unexpected(code, message, correlationId),
            _ => throw new ArgumentOutOfRangeException(nameof(factoryName))
        };

        Assert.Equal(code, failure.Code);
        Assert.Equal(message, failure.Message);
        Assert.Equal(correlationId, failure.CorrelationId);
        Assert.Equal(factoryName, failure.Category.ToString());
    }
}
