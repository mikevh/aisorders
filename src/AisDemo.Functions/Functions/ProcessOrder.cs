using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// Consumes the orders queue, advances order state, and publishes the outcome
/// to the order-events topic. See SPEC.md 7.1.
/// </summary>
public sealed class ProcessOrder
{
    /// <summary>
    /// Customer name that forces a failure, alongside the simulateFailure flag.
    /// Gives a presenter two ways into scenario 14.3 without editing a payload
    /// schema mid-demo.
    /// </summary>
    public const string FailureSentinel = "FAIL";

    private readonly OrderRepository _orders;
    private readonly OrderMessaging _messaging;
    private readonly DemoOptions _options;
    private readonly ILogger<ProcessOrder> _logger;

    public ProcessOrder(
        OrderRepository orders,
        OrderMessaging messaging,
        DemoOptions options,
        ILogger<ProcessOrder> logger)
    {
        _orders = orders;
        _messaging = messaging;
        _options = options;
        _logger = logger;
    }

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

        try
        {
            // Simulated work. Raise PROCESSING_DELAY_MS before a demo so queue
            // depth and scale-out are actually visible (scenario 14.6).
            if (_options.ProcessingDelayMs > 0)
            {
                await Task.Delay(_options.ProcessingDelayMs, cancellationToken);
            }

            // Injected failure: retried, then dead-lettered. Scenario 14.3.
            if (order.Order.SimulateFailure ||
                string.Equals(order.Order.CustomerName, FailureSentinel, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Simulated processing failure requested by the caller");
            }

            // Business-rule rejection: terminal, so it must NOT throw. Throwing
            // would retry a decision that will never change and end up
            // dead-lettering a message the system understood perfectly well.
            var rejection = Reject(order.Order);
            if (rejection is not null)
            {
                await CompleteAsync(order, OrderStatus.Rejected, rejection, attempt, cancellationToken);
                await _messaging.PublishEventAsync(
                    BuildEvent(order, OrderEvent.Types.Rejected), cancellationToken);

                _logger.LogWarning(
                    "Rejected order {OrderId}: {Reason}. CorrelationId={CorrelationId}",
                    order.OrderId, rejection, order.CorrelationId);
                return;
            }

            await CompleteAsync(order, OrderStatus.Completed, null, attempt, cancellationToken);
            await _messaging.PublishEventAsync(
                BuildEvent(order, OrderEvent.Types.Completed), cancellationToken);

            _logger.LogInformation(
                "Completed order {OrderId}. CorrelationId={CorrelationId}",
                order.OrderId, order.CorrelationId);
        }
        catch (Exception ex)
        {
            // Record the attempt before rethrowing, so a stalled order shows
            // why it stalled. After the fifth attempt nothing updates this row
            // again — it sits at Retrying/5 while the message sits in the
            // dead-letter queue, which is exactly the gap scenario 14.3 exists
            // to make visible.
            await _orders.UpdateAsync(order.OrderId, entity =>
            {
                entity.Status = nameof(OrderStatus.Retrying);
                entity.AttemptCount = attempt;
                entity.FailureReason = $"{ex.GetType().Name}: {ex.Message}";
            }, CancellationToken.None);

            _logger.LogError(ex,
                "Order {OrderId} failed on attempt {Attempt} of 5. CorrelationId={CorrelationId}",
                order.OrderId, attempt, order.CorrelationId);

            throw;
        }
    }

    private Task CompleteAsync(
        OrderMessage order,
        OrderStatus status,
        string? failureReason,
        int attempt,
        CancellationToken ct) =>
        _orders.UpdateAsync(order.OrderId, entity =>
        {
            entity.Status = status.ToString();
            entity.ProcessedAt = DateTimeOffset.UtcNow;
            entity.AttemptCount = attempt;
            entity.FailureReason = failureReason;
        }, ct);

    private static OrderEvent BuildEvent(OrderMessage order, string eventType) => new()
    {
        EventType = eventType,
        OrderId = order.OrderId,
        CustomerId = order.Order.CustomerId,
        OrderTotal = order.Order.Total(),
        CorrelationId = order.CorrelationId
    };

    /// <summary>Returns a rejection reason, or null when the order is acceptable.</summary>
    private static string? Reject(OrderSubmission submission)
    {
        if (submission.Items.Count == 0)
        {
            return "order contains no items";
        }

        return submission.Total() <= 0 ? "order total must be greater than zero" : null;
    }
}
