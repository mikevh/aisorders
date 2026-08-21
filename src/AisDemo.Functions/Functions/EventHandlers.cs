using System.Diagnostics;
using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// Filtered subscriber on the order-events topic.
/// </summary>
/// <remarks>
/// Only receives events matching the subscription's SQL filter — completed
/// orders above the notification threshold. A small order never reaches this
/// handler at all, which is the visible half of demo scenario 14.2.
/// </remarks>
public sealed class NotificationHandler
{
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(ILogger<NotificationHandler> logger) => _logger = logger;

    [Function(nameof(NotificationHandler))]
    public void Run(
        [ServiceBusTrigger("%ORDER_EVENTS_TOPIC%", "%SUBSCRIPTION_NOTIFICATIONS%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
    {
        var evt = JsonDefaults.Deserialize<OrderEvent>(message.Body.ToString());
        if (evt is null)
        {
            _logger.LogWarning("Notification message {MessageId} had no readable body", message.MessageId);
            return;
        }

        // Tagged on the current span so the notification is attributable to one
        // order in the end-to-end transaction view, not just a log line.
        Telemetry.TagOrder(evt.OrderId, evt.CorrelationId);
        Activity.Current?.SetTag("eventType", evt.EventType);

        _logger.LogInformation(
            "Notifying customer {CustomerId} that order {OrderId} completed at {OrderTotal}. CorrelationId={CorrelationId}",
            evt.CustomerId, evt.OrderId, evt.OrderTotal, evt.CorrelationId);
    }
}

/// <summary>
/// Catch-all subscriber on the order-events topic, writing to AuditLog.
/// </summary>
/// <remarks>
/// Its subscription keeps the default TrueFilter, so it receives every event
/// including those the notifications filter excludes. The pair is what makes
/// filtering demonstrable: submit a small order and only this handler runs.
/// </remarks>
public sealed class AuditHandler
{
    private readonly AuditRepository _audit;
    private readonly ILogger<AuditHandler> _logger;

    public AuditHandler(AuditRepository audit, ILogger<AuditHandler> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    [Function(nameof(AuditHandler))]
    public async Task Run(
        [ServiceBusTrigger("%ORDER_EVENTS_TOPIC%", "%SUBSCRIPTION_AUDIT%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var body = message.Body.ToString();
        var evt = JsonDefaults.Deserialize<OrderEvent>(body);
        if (evt is null)
        {
            _logger.LogWarning("Audit message {MessageId} had no readable body", message.MessageId);
            return;
        }

        Telemetry.TagOrder(evt.OrderId, evt.CorrelationId);
        Activity.Current?.SetTag("eventType", evt.EventType);

        await _audit.AppendAsync(evt, body, cancellationToken);

        _logger.LogInformation(
            "Audited {EventType} for order {OrderId}. CorrelationId={CorrelationId}",
            evt.EventType, evt.OrderId, evt.CorrelationId);
    }
}
