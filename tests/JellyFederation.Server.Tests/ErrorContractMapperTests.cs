using JellyFederation.Server.Controllers;
using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ErrorContractMapperTests
{
    [Theory]
    [InlineData(FailureCategory.Validation, 400)]
    [InlineData(FailureCategory.Authorization, 403)]
    [InlineData(FailureCategory.NotFound, 404)]
    [InlineData(FailureCategory.Conflict, 409)]
    [InlineData(FailureCategory.Connectivity, 503)]
    [InlineData(FailureCategory.Timeout, 503)]
    [InlineData(FailureCategory.Reliability, 503)]
    [InlineData(FailureCategory.Cancelled, 409)]
    [InlineData(FailureCategory.Unexpected, 500)]
    public void ToStatusCode_MapsFailureCategory(FailureCategory category, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ErrorContractMapper.ToStatusCode(category));
    }

    [Fact]
    public void ToActionResult_ProducesErrorEnvelopeAndStatusCode()
    {
        var failure = FailureDescriptor.Authorization("auth.denied", "forbidden", "corr-1");

        var result = ErrorContractMapper.ToActionResult(failure);

        Assert.Equal(403, result.StatusCode);
        var envelope = Assert.IsType<ErrorEnvelope>(result.Value);
        Assert.Equal("auth.denied", envelope.Error.Code);
        Assert.Equal(nameof(FailureCategory.Authorization), envelope.Error.Category);
        Assert.Equal("corr-1", envelope.Error.CorrelationId);
    }

    [Fact]
    public void SetRequestableRequest_DeconstructsExpectedValue()
    {
        var request = new SetRequestableRequest(true);

        request.Deconstruct(out var isRequestable);

        Assert.True(isRequestable);
        Assert.True(request.IsRequestable);
    }
}
