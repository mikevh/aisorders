using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// POST /api/dlq/replay — drains the orders dead-letter queue and resubmits.
/// </summary>
/// <remarks>
/// The operational half of demo scenario 14.3. Watching a message dead-letter
/// shows the failure; draining and replaying it shows the recovery, which is
/// the part an operations audience actually asks about.
///
/// The route is dlq/replay, not admin/replay. The Functions host reserves the
/// "admin" route segment for its own management endpoints, and a function
/// route beginning with it registers cleanly, appears in /admin/functions, and
/// then returns 404 for every request - including with the master key. The
/// public path stays /admin/replay; APIM rewrites it (SPEC.md 5.1).
/// </remarks>
public sealed class ReplayDeadLetter
{
    private const int DefaultMax = 10;
    private const int HardCap = 100;

    private readonly OrderMessaging _messaging;
    private readonly OrderRepository _orders;
    private readonly ILogger<ReplayDeadLetter> _logger;

    public ReplayDeadLetter(OrderMessaging messaging, OrderRepository orders, ILogger<ReplayDeadLetter> logger)
    {
        _messaging = messaging;
        _orders = orders;
        _logger = logger;
    }

    [Function(nameof(ReplayDeadLetter))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "dlq/replay")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var remediate = !(request.Query.TryGetValue("remediate", out var rem)
            && string.Equals(rem.ToString(), "false", StringComparison.OrdinalIgnoreCase));

        var max = DefaultMax;
        if (request.Query.TryGetValue("max", out var raw) && int.TryParse(raw, out var parsed))
        {
            max = Math.Clamp(parsed, 1, HardCap);
        }

        await using var receiver = _messaging.CreateDeadLetterReceiver();

        var messages = await receiver.ReceiveMessagesAsync(
            max, TimeSpan.FromSeconds(5), cancellationToken);

        var orderIds = new List<string>();
        var resubmitted = 0;

        foreach (var message in messages)
        {
            var order = TryRead(message.Body.ToString());

            if (order is null)
            {
                // Unreadable messages are the ones scenario 14.4 injects. They
                // are completed rather than resubmitted: replaying something
                // that cannot be deserialized just sends it straight back to
                // the dead-letter queue.
                _logger.LogWarning(
                    "Discarding unreadable dead-lettered message {MessageId}: {Reason}",
                    message.MessageId, message.DeadLetterReason ?? "unknown");
                await receiver.CompleteMessageAsync(message, cancellationToken);
                continue;
            }

            // Clear the injected failure before resubmitting, unless the
            // caller asks otherwise with ?remediate=false.
            //
            // Replaying a poison message unchanged simply poisons it again -
            // realistic, but it leaves scenario 14.3 with no recovery to show.
            // Treat this as standing in for the real remediation an operator
            // would perform before draining a dead-letter queue; the runbook
            // says so out loud rather than letting the demo imply that replay
            // fixes anything by itself.
            var toSend = remediate ? Remediate(order) : order;

            await _messaging.SendOrderAsync(toSend, cancellationToken);

            // Complete only after the resubmit succeeds. The reverse order
            // could drop an order entirely if the send failed.
            await receiver.CompleteMessageAsync(message, cancellationToken);

            await _orders.UpdateAsync(order.OrderId, entity =>
            {
                entity.Status = nameof(OrderStatus.Replayed);
                entity.FailureReason = null;
            }, cancellationToken);

            Telemetry.TagOrder(order.OrderId, order.CorrelationId, nameof(OrderStatus.Replayed));

            orderIds.Add(order.OrderId);
            resubmitted++;

            _logger.LogInformation(
                "Replayed order {OrderId} from the dead-letter queue. CorrelationId={CorrelationId}",
                order.OrderId, order.CorrelationId);
        }

        _logger.LogInformation(
            "Replay drained {Drained} message(s), resubmitted {Resubmitted}",
            messages.Count, resubmitted);

        return new OkObjectResult(new ReplayResult
        {
            Drained = messages.Count,
            Resubmitted = resubmitted,
            OrderIds = orderIds
        });
    }

    /// <summary>
    /// Returns the order with any injected failure switch cleared.
    /// </summary>
    private static OrderMessage Remediate(OrderMessage order) => order with
    {
        Order = order.Order with
        {
            SimulateFailure = false,
            CustomerName = string.Equals(order.Order.CustomerName, ProcessOrder.FailureSentinel,
                StringComparison.OrdinalIgnoreCase)
                    ? $"{order.Order.CustomerName} (remediated)"
                    : order.Order.CustomerName
        }
    };

    private static OrderMessage? TryRead(string body)
    {
        try
        {
            var order = JsonDefaults.Deserialize<OrderMessage>(body);
            return string.IsNullOrWhiteSpace(order?.OrderId) ? null : order;
        }
        catch
        {
            return null;
        }
    }
}
