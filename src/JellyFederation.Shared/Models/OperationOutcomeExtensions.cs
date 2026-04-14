namespace JellyFederation.Shared.Models;

public static class OperationOutcomeExtensions
{
    // Usage examples:
    // var titleOutcome = await GetItemAsync(id).MapAsync(item => Task.FromResult(item.Title));
    // var persisted = await ValidateAsync(request).BindAsync(valid => SaveAsync(valid));
    // var httpResult = outcome.Match(
    //     success => Results.Ok(success),
    //     failure => Results.Problem(failure.Message));
    public static OperationOutcome<TMapped> Map<TSource, TMapped>(
        this OperationOutcome<TSource> outcome,
        Func<TSource, TMapped> mapper)
    {
        if (outcome.IsFailure)
            return OperationOutcome<TMapped>.Fail(outcome.Failure!);

        return OperationOutcome<TMapped>.Success(mapper(outcome.RequireValue()));
    }

    public static OperationOutcome<TMapped> Bind<TSource, TMapped>(
        this OperationOutcome<TSource> outcome,
        Func<TSource, OperationOutcome<TMapped>> binder)
    {
        if (outcome.IsFailure)
            return OperationOutcome<TMapped>.Fail(outcome.Failure!);

        return binder(outcome.RequireValue());
    }

    public static async Task<OperationOutcome<TMapped>> MapAsync<TSource, TMapped>(
        this Task<OperationOutcome<TSource>> outcomeTask,
        Func<TSource, Task<TMapped>> mapper)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        if (outcome.IsFailure)
            return OperationOutcome<TMapped>.Fail(outcome.Failure!);

        return OperationOutcome<TMapped>.Success(await mapper(outcome.RequireValue()).ConfigureAwait(false));
    }

    public static async Task<OperationOutcome<TMapped>> BindAsync<TSource, TMapped>(
        this Task<OperationOutcome<TSource>> outcomeTask,
        Func<TSource, Task<OperationOutcome<TMapped>>> binder)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        if (outcome.IsFailure)
            return OperationOutcome<TMapped>.Fail(outcome.Failure!);

        return await binder(outcome.RequireValue()).ConfigureAwait(false);
    }

    public static TResult Match<TSource, TResult>(
        this OperationOutcome<TSource> outcome,
        Func<TSource, TResult> onSuccess,
        Func<FailureDescriptor, TResult> onFailure)
    {
        return outcome.IsSuccess
            ? onSuccess(outcome.RequireValue())
            : onFailure(outcome.Failure!);
    }

    public static async Task<TResult> MatchAsync<TSource, TResult>(
        this Task<OperationOutcome<TSource>> outcomeTask,
        Func<TSource, Task<TResult>> onSuccess,
        Func<FailureDescriptor, Task<TResult>> onFailure)
    {
        var outcome = await outcomeTask.ConfigureAwait(false);
        return outcome.IsSuccess
            ? await onSuccess(outcome.RequireValue()).ConfigureAwait(false)
            : await onFailure(outcome.Failure!).ConfigureAwait(false);
    }
}
