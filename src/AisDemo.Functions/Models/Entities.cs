using Azure;
using Azure.Data.Tables;

namespace AisDemo.Functions.Models;

/// <summary>
/// A row in the Orders table. See SPEC.md 8.
/// </summary>
/// <remarks>
/// <para>
/// PartitionKey is the fixed literal "ORDER" so a status lookup needs only the
/// orderId. In production that is a hot-partition antipattern; it is kept
/// deliberately as a demo talking point rather than hidden.
/// </para>
/// <para>
/// OrderTotal is a double, not a decimal, because Azure Table Storage has no
/// decimal EDM type — the supported set is string, bool, DateTime, double,
/// Guid, Int32, Int64, and binary. Domain logic works in decimal and converts
/// only at this boundary. For two-decimal demo values the rounding is
/// imperceptible; real money would be stored as integer minor units instead.
/// </para>
/// </remarks>
public sealed class OrderEntity : ITableEntity
{
    public const string Partition = "ORDER";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = nameof(OrderStatus.Accepted);
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public double OrderTotal { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? FailureReason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string ItemsJson { get; set; } = "[]";

    public OrderStatus StatusValue =>
        Enum.TryParse<OrderStatus>(Status, out var parsed) ? parsed : OrderStatus.Accepted;
}

/// <summary>
/// A row in the AuditLog table, written by AuditHandler for every event the
/// catch-all subscription receives.
/// </summary>
public sealed class AuditEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string EventType { get; set; } = string.Empty;
    public double OrderTotal { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";

    /// <summary>
    /// Partitioned by order so an order's full audit trail is a single
    /// partition scan; row key sorts chronologically within it.
    /// </summary>
    public static AuditEntity For(OrderEvent evt, string payloadJson) => new()
    {
        PartitionKey = evt.OrderId,
        RowKey = $"{evt.OccurredAt:O}-{evt.EventId}",
        EventType = evt.EventType,
        OrderTotal = (double)evt.OrderTotal,
        CustomerId = evt.CustomerId,
        CorrelationId = evt.CorrelationId,
        PayloadJson = payloadJson
    };
}
