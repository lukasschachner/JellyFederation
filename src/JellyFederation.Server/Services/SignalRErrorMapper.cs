using JellyFederation.Shared.Models;

namespace JellyFederation.Server.Services;

public sealed class SignalRErrorMapper
{
    public static ErrorContract? ToContract(FailureDescriptor? failure) =>
        failure is null ? null : ErrorContractMapper.ToContract(failure);
}