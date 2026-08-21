namespace AisDemo.Functions.Models;

/// <summary>202 body returned by SubmitOrder. See SPEC.md 5.2.</summary>
public sealed record OrderAccepted
{
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = nameof(OrderStatus.Accepted);
    public string CorrelationId { get; init; } = string.Empty;
    public string StatusUrl { get; init; } = string.Empty;
}

/// <summary>200 body returned by GetOrder.</summary>
public sealed record OrderStatusResponse
{
    public string OrderId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal OrderTotal { get; init; }
    public int ItemCount { get; init; }
    public DateTimeOffset SubmittedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public int AttemptCount { get; init; }
    public string? FailureReason { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    public static OrderStatusResponse From(OrderEntity e) => new()
    {
        OrderId = e.RowKey,
        Status = e.Status,
        CustomerId = e.CustomerId,
        CustomerName = e.CustomerName,
        OrderTotal = (decimal)e.OrderTotal,
        ItemCount = e.ItemCount,
        SubmittedAt = e.SubmittedAt,
        ProcessedAt = e.ProcessedAt,
        AttemptCount = e.AttemptCount,
        FailureReason = e.FailureReason,
        CorrelationId = e.CorrelationId
    };
}

/// <summary>200 body returned by ReplayDeadLetter.</summary>
public sealed record ReplayResult
{
    public int Drained { get; init; }
    public int Resubmitted { get; init; }
    public IReadOnlyList<string> OrderIds { get; init; } = [];
}

/// <summary>
/// RFC 7807 problem detail. The gateway shapes its own errors (SPEC.md 5.1);
/// this is the equivalent for failures raised inside the functions.
/// </summary>
public sealed record ProblemDetail
{
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string? Detail { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}
