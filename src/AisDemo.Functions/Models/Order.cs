using System.Text.Json.Serialization;

namespace AisDemo.Functions.Models;

/// <summary>A single line on a submitted order.</summary>
public sealed record OrderItem
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }

    // Derived, so kept out of serialized payloads. Persisting it alongside its
    // own inputs invites drift and puts a redundant column in front of anyone
    // reading the Orders table during a demo.
    [JsonIgnore]
    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>
/// The request body accepted by POST /orders. See SPEC.md 5.2.
/// </summary>
public sealed record OrderSubmission
{
    public string CustomerId { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public IReadOnlyList<OrderItem> Items { get; init; } = [];

    /// <summary>
    /// Drives demo scenario 14.3. When true, ProcessOrder throws, so the
    /// message retries and eventually dead-letters.
    /// </summary>
    public bool SimulateFailure { get; init; }

    public decimal Total() => Items.Sum(i => i.LineTotal);
    public int ItemCount() => Items.Sum(i => i.Quantity);
}

/// <summary>
/// Lifecycle states from SPEC.md 5.3. An order that exhausts every delivery
/// attempt stays at <see cref="Retrying"/> — nothing updates the row once the
/// message dead-letters, which is deliberate and is the point of scenario 14.3.
/// </summary>
public enum OrderStatus
{
    Accepted,
    Processing,
    Retrying,
    Completed,
    Rejected,
    Replayed
}

/// <summary>
/// What travels on the orders queue: the submission plus the identifiers
/// assigned at the gateway and by SubmitOrder.
/// </summary>
public sealed record OrderMessage
{
    public string OrderId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public OrderSubmission Order { get; init; } = new();
}

/// <summary>
/// Published to the order-events topic after each terminal outcome.
/// </summary>
/// <remarks>
/// EventType, OrderTotal, and CustomerId are also set as Service Bus
/// application properties by the publisher. Subscription filters read message
/// properties, not the body — setting them only here would leave the
/// notifications subscription silently receiving nothing. See SPEC.md 6.2.
/// </remarks>
public sealed record OrderEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public string EventType { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal OrderTotal { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;

    public static class Types
    {
        public const string Completed = "OrderCompleted";
        public const string Rejected = "OrderRejected";
    }
}
