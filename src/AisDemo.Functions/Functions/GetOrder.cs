using AisDemo.Functions.Models;
using AisDemo.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Functions;

/// <summary>
/// GET /api/orders/{orderId} — a point read of the Orders table. See SPEC.md 5.2.
/// </summary>
public sealed class GetOrder
{
    private readonly OrderRepository _orders;
    private readonly ILogger<GetOrder> _logger;

    public GetOrder(OrderRepository orders, ILogger<GetOrder> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    [Function(nameof(GetOrder))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/{orderId}")] HttpRequest request,
        string orderId,
        CancellationToken cancellationToken)
    {
        var correlationId = request.Headers.TryGetValue(SubmitOrder.CorrelationHeader, out var header)
            && !string.IsNullOrWhiteSpace(header.ToString())
                ? header.ToString()
                : Guid.NewGuid().ToString();

        request.HttpContext.Response.Headers[SubmitOrder.CorrelationHeader] = correlationId;

        var entity = await _orders.GetAsync(orderId, cancellationToken);
        if (entity is null)
        {
            _logger.LogInformation("Order {OrderId} not found", orderId);
            return new ObjectResult(new ProblemDetail
            {
                Title = "Order not found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"No order with id {orderId}.",
                CorrelationId = correlationId
            })
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentTypes = { "application/problem+json" }
            };
        }

        return new OkObjectResult(OrderStatusResponse.From(entity));
    }
}
