namespace JellyFederation.Shared.Models;

public sealed class OperationOutcome<TSuccess>
{
    private OperationOutcome(bool isSuccess, TSuccess? value, FailureDescriptor? failure)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public TSuccess? Value { get; }
    public FailureDescriptor? Failure { get; }

    public static OperationOutcome<TSuccess> Success(TSuccess value)
    {
        return new OperationOutcome<TSuccess>(true, value, null);
    }

    public static OperationOutcome<TSuccess> Fail(FailureDescriptor failure)
    {
        return new OperationOutcome<TSuccess>(false, default, failure);
    }

    public TSuccess RequireValue()
    {
        if (!IsSuccess || Value is null)
            throw new InvalidOperationException("Cannot access success value for a failed operation.");
        return Value;
    }
}
