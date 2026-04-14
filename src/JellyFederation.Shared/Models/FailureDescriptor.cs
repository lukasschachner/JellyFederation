namespace JellyFederation.Shared.Models;

public enum FailureCategory
{
    Validation = 0,
    Authorization = 1,
    NotFound = 2,
    Conflict = 3,
    Connectivity = 4,
    Timeout = 5,
    Cancelled = 6,
    Reliability = 7,
    Unexpected = 8
}

public sealed record FailureDescriptor
{
    public FailureDescriptor(
        string Code,
        FailureCategory Category,
        string Message,
        string? CorrelationId = null,
        Dictionary<string, string?>? Details = null)
    {
        this.Code = Code;
        this.Category = Category;
        this.Message = Message;
        this.CorrelationId = CorrelationId;
        this.Details = Details;
    }

    public string Code { get; init; }
    public FailureCategory Category { get; init; }
    public string Message { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string?>? Details { get; init; }

    public static FailureDescriptor Validation(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Validation, message, correlationId);
    }

    public static FailureDescriptor Authorization(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Authorization, message, correlationId);
    }

    public static FailureDescriptor NotFound(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.NotFound, message, correlationId);
    }

    public static FailureDescriptor Conflict(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Conflict, message, correlationId);
    }

    public static FailureDescriptor Connectivity(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Connectivity, message, correlationId);
    }

    public static FailureDescriptor Timeout(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Timeout, message, correlationId);
    }

    public static FailureDescriptor Cancelled(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Cancelled, message, correlationId);
    }

    public static FailureDescriptor Reliability(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Reliability, message, correlationId);
    }

    public static FailureDescriptor Unexpected(string code, string message, string? correlationId = null)
    {
        return new FailureDescriptor(code, FailureCategory.Unexpected, message, correlationId);
    }
}

public sealed record ErrorContract(
    string Code,
    string Category,
    string Message,
    string? CorrelationId = null,
    Dictionary<string, string?>? Details = null);

public sealed record ErrorEnvelope(ErrorContract Error);
