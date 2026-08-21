using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// Consumes the orders queue and advances order state.
/// </summary>
/// <remarks>
/// Minimal by design at W13: Processing then Completed. The simulated delay,
/// failure injection, business-rule rejection, the Retrying path, and topic
/// publication all arrive in W24, once the topic exists.
/// </remarks>
public sealed class ProcessOrder
{
    private readonly OrderRepository _orders;
    private readonly ILogger<ProcessOrder> _logger;

    public ProcessOrder(OrderRepository orders, ILogger<ProcessOrder> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    // The connection name resolves against ServiceBusConnection__fullyQualifiedNamespace
    // when deployed, so the host authenticates with the system-assigned
    // identity. Locally the same name picks up a plain connection string.
    [Function(nameof(ProcessOrder))]
    public async Task Run(
        [ServiceBusTrigger("%ORDERS_QUEUE%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var attempt = (int)message.DeliveryCount;

        OrderMessage? order;
        try
        {
            order = JsonDefaults.Deserialize<OrderMessage>(message.Body.ToString());
        }
        catch (Exception ex)
        {
            // Scenario 14.4: a message injected straight onto the queue,
            // bypassing the gateway, fails here rather than in business logic.
            // Rethrowing lets Service Bus retry and eventually dead-letter it.
            _logger.LogError(ex,
                "Could not deserialize message {MessageId} on attempt {Attempt}",
                message.MessageId, attempt);
            throw;
        }

        if (order is null || string.IsNullOrWhiteSpace(order.OrderId))
        {
            throw new InvalidOperationException(
                $"Message {message.MessageId} carried no usable order payload");
        }

        _logger.LogInformation(
            "Processing order {OrderId}, attempt {Attempt}. CorrelationId={CorrelationId}",
            order.OrderId, attempt, order.CorrelationId);

        await _orders.UpdateAsync(order.OrderId, entity =>
        {
            entity.Status = nameof(OrderStatus.Processing);
            entity.AttemptCount = attempt;
        }, cancellationToken);

        var updated = await _orders.UpdateAsync(order.OrderId, entity =>
        {
            entity.Status = nameof(OrderStatus.Completed);
            entity.ProcessedAt = DateTimeOffset.UtcNow;
            entity.FailureReason = null;
        }, cancellationToken);

        if (updated is null)
        {
            _logger.LogWarning(
                "Order {OrderId} had no row to update; it may have been deleted",
                order.OrderId);
            return;
        }

        _logger.LogInformation(
            "Completed order {OrderId}. CorrelationId={CorrelationId}",
            order.OrderId, order.CorrelationId);
    }
}
