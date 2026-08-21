using AisDemo.Functions.Models;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Services;

/// <summary>
/// Reads and writes order state in the Orders table (SPEC.md 8).
/// </summary>
public sealed class OrderRepository
{
    private readonly TableClient _table;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(TableServiceClient service, DemoOptions options, ILogger<OrderRepository> logger)
    {
        _table = service.GetTableClient(options.OrdersTable);
        _logger = logger;
    }

    /// <summary>
    /// Terraform creates the tables in Azure, but Azurite starts empty — so
    /// the local path needs this and the deployed path is a no-op.
    /// </summary>
    public Task EnsureExistsAsync(CancellationToken ct = default) =>
        _table.CreateIfNotExistsAsync(ct);

    public async Task<OrderEntity?> GetAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<OrderEntity>(
                OrderEntity.Partition, orderId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync(OrderEntity entity, CancellationToken ct = default) =>
        _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

    /// <summary>
    /// Applies <paramref name="mutate"/> to the stored order and saves it.
    /// Returns null when the order does not exist.
    /// </summary>
    /// <remarks>
    /// Deliberately a last-write-wins upsert rather than an ETag-guarded
    /// update. Service Bus redelivery means two workers can briefly hold the
    /// same order, and failing the second one would dead-letter a message for
    /// a bookkeeping conflict rather than a real fault — the wrong lesson for
    /// scenario 14.3 to teach.
    /// </remarks>
    public async Task<OrderEntity?> UpdateAsync(
        string orderId,
        Action<OrderEntity> mutate,
        CancellationToken ct = default)
    {
        var entity = await GetAsync(orderId, ct);
        if (entity is null)
        {
            _logger.LogWarning("Order {OrderId} not found when applying an update", orderId);
            return null;
        }

        mutate(entity);
        await UpsertAsync(entity, ct);
        return entity;
    }
}

/// <summary>
/// Appends to the AuditLog table. Written by AuditHandler for every event the
/// catch-all subscription receives.
/// </summary>
public sealed class AuditRepository
{
    private readonly TableClient _table;

    public AuditRepository(TableServiceClient service, DemoOptions options)
    {
        _table = service.GetTableClient(options.AuditTable);
    }

    public Task EnsureExistsAsync(CancellationToken ct = default) =>
        _table.CreateIfNotExistsAsync(ct);

    public Task AppendAsync(OrderEvent evt, string payloadJson, CancellationToken ct = default) =>
        _table.UpsertEntityAsync(AuditEntity.For(evt, payloadJson), TableUpdateMode.Replace, ct);
}
