using JellyFederation.Shared.Models;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class OperationOutcomeExtensionsTests
{
    [Fact]
    public void Map_WhenSuccess_TransformsValue()
    {
        var outcome = OperationOutcome<int>.Success(21);

        var mapped = outcome.Map(x => x * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.RequireValue());
    }

    [Fact]
    public void Map_WhenFailure_DoesNotInvokeMapper()
    {
        var failure = FailureDescriptor.Validation("map.failed", "failed");
        var outcome = OperationOutcome<int>.Fail(failure);
        var called = false;

        var mapped = outcome.Map(_ =>
        {
            called = true;
            return 1;
        });

        Assert.False(called);
        Assert.True(mapped.IsFailure);
        Assert.Equal(failure, mapped.Failure);
    }

    [Fact]
    public void Bind_WhenSuccess_ReturnsBinderOutcome()
    {
        var outcome = OperationOutcome<int>.Success(5);

        var bound = outcome.Bind(x => OperationOutcome<string>.Success($"{x}:ok"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("5:ok", bound.RequireValue());
    }

    [Fact]
    public void Bind_WhenFailure_DoesNotInvokeBinder()
    {
        var failure = FailureDescriptor.Conflict("bind.failed", "failed");
        var outcome = OperationOutcome<int>.Fail(failure);
        var called = false;

        var bound = outcome.Bind(_ =>
        {
            called = true;
            return OperationOutcome<string>.Success("ignored");
        });

        Assert.False(called);
        Assert.True(bound.IsFailure);
        Assert.Equal(failure, bound.Failure);
    }

    [Fact]
    public async Task MapAsync_WhenSuccess_TransformsValue()
    {
        var mapped = await Task.FromResult(OperationOutcome<int>.Success(10))
            .MapAsync(x => Task.FromResult(x + 1));

        Assert.True(mapped.IsSuccess);
        Assert.Equal(11, mapped.RequireValue());
    }

    [Fact]
    public async Task MapAsync_WhenFailure_DoesNotInvokeMapper()
    {
        var failure = FailureDescriptor.Timeout("map.async.failed", "failed");
        var called = false;

        var mapped = await Task.FromResult(OperationOutcome<int>.Fail(failure))
            .MapAsync(x =>
            {
                called = true;
                return Task.FromResult(x + 1);
            });

        Assert.False(called);
        Assert.True(mapped.IsFailure);
        Assert.Equal(failure, mapped.Failure);
    }

    [Fact]
    public async Task BindAsync_WhenSuccess_ReturnsBinderOutcome()
    {
        var bound = await Task.FromResult(OperationOutcome<int>.Success(3))
            .BindAsync(x => Task.FromResult(OperationOutcome<string>.Success($"{x}-bound")));

        Assert.True(bound.IsSuccess);
        Assert.Equal("3-bound", bound.RequireValue());
    }

    [Fact]
    public async Task BindAsync_WhenFailure_DoesNotInvokeBinder()
    {
        var failure = FailureDescriptor.NotFound("bind.async.failed", "failed");
        var called = false;

        var bound = await Task.FromResult(OperationOutcome<int>.Fail(failure))
            .BindAsync(x =>
            {
                called = true;
                return Task.FromResult(OperationOutcome<string>.Success(x.ToString()));
            });

        Assert.False(called);
        Assert.True(bound.IsFailure);
        Assert.Equal(failure, bound.Failure);
    }

    [Fact]
    public void Match_SelectsExpectedBranch()
    {
        var success = OperationOutcome<int>.Success(7)
            .Match(v => $"ok:{v}", f => $"fail:{f.Code}");
        var failure = OperationOutcome<int>.Fail(FailureDescriptor.Unexpected("boom", "nope"))
            .Match(v => $"ok:{v}", f => $"fail:{f.Code}");

        Assert.Equal("ok:7", success);
        Assert.Equal("fail:boom", failure);
    }

    [Fact]
    public async Task MatchAsync_SelectsExpectedBranch()
    {
        var success = await Task.FromResult(OperationOutcome<int>.Success(8))
            .MatchAsync(v => Task.FromResult($"ok:{v}"), f => Task.FromResult($"fail:{f.Code}"));
        var failure = await Task.FromResult(OperationOutcome<int>.Fail(FailureDescriptor.Cancelled("cancelled", "x")))
            .MatchAsync(v => Task.FromResult($"ok:{v}"), f => Task.FromResult($"fail:{f.Code}"));

        Assert.Equal("ok:8", success);
        Assert.Equal("fail:cancelled", failure);
    }

    [Fact]
    public void Map_WhenMapperThrows_ExceptionBubbles()
    {
        var outcome = OperationOutcome<int>.Success(1);

        var ex = Assert.Throws<InvalidOperationException>(() => outcome.Map<int, int>(_ => throw new InvalidOperationException("mapper")));

        Assert.Equal("mapper", ex.Message);
    }
}
