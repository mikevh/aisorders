using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// POST /api/orders — accepts an order, persists it, and enqueues it for
/// asynchronous processing. See SPEC.md 3.1 and 5.2.
/// </summary>
public sealed class SubmitOrder
{
    public const string CorrelationHeader = "x-correlation-id";

    private readonly OrderRepository _orders;
    private readonly OrderMessaging _messaging;
    private readonly ILogger<SubmitOrder> _logger;

    public SubmitOrder(OrderRepository orders, OrderMessaging messaging, ILogger<SubmitOrder> logger)
    {
        _orders = orders;
        _messaging = messaging;
        _logger = logger;
    }

    [Function(nameof(SubmitOrder))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        // APIM generates this and overrides anything the caller sent (SPEC.md
        // 5.1), so by the time it arrives here it is trusted. A direct call
        // that bypasses the gateway gets one minted here instead.
        var correlationId = request.Headers.TryGetValue(CorrelationHeader, out var header)
            && !string.IsNullOrWhiteSpace(header.ToString())
                ? header.ToString()
                : Guid.NewGuid().ToString();

        request.HttpContext.Response.Headers[CorrelationHeader] = correlationId;

        OrderSubmission? submission;
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            submission = JsonDefaults.Deserialize<OrderSubmission>(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed request body. CorrelationId={CorrelationId}", correlationId);
            return Problem("Malformed request body", StatusCodes.Status400BadRequest, ex.Message, correlationId);
        }

        if (submission is null)
        {
            return Problem("Request body is required", StatusCodes.Status400BadRequest, null, correlationId);
        }

        if (Validate(submission) is { } validationError)
        {
            _logger.LogWarning(
                "Rejected invalid order from {CustomerId}: {Reason}. CorrelationId={CorrelationId}",
                submission.CustomerId, validationError, correlationId);
            return Problem("Invalid order", StatusCodes.Status400BadRequest, validationError, correlationId);
        }

        var orderId = Guid.NewGuid().ToString();
        var total = submission.Total();

        var entity = new OrderEntity
        {
            RowKey = orderId,
            Status = nameof(OrderStatus.Accepted),
            CustomerId = submission.CustomerId,
            CustomerName = submission.CustomerName,
            OrderTotal = (double)total,
            ItemCount = submission.ItemCount(),
            SubmittedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            CorrelationId = correlationId,
            ItemsJson = JsonDefaults.Serialize(submission.Items)
        };

        // Persist before enqueueing. The reverse order allows a processor to
        // pick the message up before any row exists to update.
        //
        // Wrapped because an unhandled exception here surfaces as a bare 500
        // with an empty body, and — in OpenTelemetry mode — leaves no
        // AppExceptions row and no failed-dependency span to diagnose from.
        // Logging it explicitly is the difference between a diagnosable
        // failure and a silent one.
        try
        {
            await _orders.UpsertAsync(entity, cancellationToken);

            await _messaging.SendOrderAsync(
                new OrderMessage { OrderId = orderId, CorrelationId = correlationId, Order = submission },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to accept order {OrderId} for {CustomerId}: {ExceptionType}: {ExceptionMessage}. CorrelationId={CorrelationId}",
                orderId, submission.CustomerId, ex.GetType().FullName, ex.Message, correlationId);

            return Problem(
                "Could not accept the order",
                StatusCodes.Status500InternalServerError,
                $"{ex.GetType().Name}: {ex.Message}",
                correlationId);
        }

        _logger.LogInformation(
            "Accepted order {OrderId} for {CustomerId}, total {OrderTotal}. CorrelationId={CorrelationId}",
            orderId, submission.CustomerId, total, correlationId);

        var statusUrl = $"/orders/{orderId}";
        request.HttpContext.Response.Headers.Location = statusUrl;

        return new ObjectResult(new OrderAccepted
        {
            OrderId = orderId,
            Status = nameof(OrderStatus.Accepted),
            CorrelationId = correlationId,
            StatusUrl = statusUrl
        })
        {
            StatusCode = StatusCodes.Status202Accepted
        };
    }

    /// <summary>
    /// Returns a description of the first problem found, or null when valid.
    /// </summary>
    /// <remarks>
    /// Validation lives here rather than in an APIM content-validation policy.
    /// SPEC.md 17 risk 7 flagged that policy's tier support as unverified, and
    /// scenario 14.4 injects a malformed message straight onto the queue
    /// anyway — so the backend has to handle bad input regardless.
    /// </remarks>
    private static string? Validate(OrderSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.CustomerId))
        {
            return "customerId is required";
        }

        if (submission.Items.Count == 0)
        {
            return "at least one item is required";
        }

        if (submission.Items.Any(i => string.IsNullOrWhiteSpace(i.Sku)))
        {
            return "every item requires a sku";
        }

        if (submission.Items.Any(i => i.Quantity <= 0))
        {
            return "item quantity must be greater than zero";
        }

        if (submission.Items.Any(i => i.UnitPrice < 0))
        {
            return "item unitPrice cannot be negative";
        }

        return submission.Total() <= 0 ? "order total must be greater than zero" : null;
    }

    private static ObjectResult Problem(string title, int status, string? detail, string correlationId) =>
        new(new ProblemDetail
        {
            Title = title,
            Status = status,
            Detail = detail,
            CorrelationId = correlationId
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
