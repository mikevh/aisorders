using Microsoft.Extensions.Configuration;

namespace AisDemo.Functions.Services;

/// <summary>
/// Typed view over the app settings in SPEC.md 15.2.
/// </summary>
/// <remarks>
/// Note the two Service Bus keys. .NET configuration turns the double
/// underscore in <c>ServiceBusConnection__fullyQualifiedNamespace</c> into a
/// colon, so the deployed identity-based setting is read as
/// <c>ServiceBusConnection:fullyQualifiedNamespace</c>. Locally the emulator
/// supplies a plain connection string under <c>ServiceBusConnection</c>
/// instead. Which one is populated is the entire local-versus-Azure
/// difference, and <see cref="AzureClientFactory"/> is the only place that
/// decides between them.
/// </remarks>
public sealed class DemoOptions
{
    public string? ServiceBusConnectionString { get; init; }
    public string? ServiceBusFullyQualifiedNamespace { get; init; }
    public string? StorageConnectionString { get; init; }
    public string? StorageAccountName { get; init; }

    public string QueueName { get; init; } = "orders";
    public string TopicName { get; init; } = "order-events";
    public string OrdersTable { get; init; } = "Orders";
    public string AuditTable { get; init; } = "AuditLog";
    public int ProcessingDelayMs { get; init; } = 250;

    /// <summary>
    /// True when running against the emulator or Azurite, where credentials
    /// come from a connection string rather than a managed identity.
    /// </summary>
    public bool UsesLocalEmulators =>
        !string.IsNullOrWhiteSpace(ServiceBusConnectionString) ||
        !string.IsNullOrWhiteSpace(StorageConnectionString);

    public static DemoOptions FromConfiguration(IConfiguration config) => new()
    {
        ServiceBusConnectionString = Trimmed(config["ServiceBusConnection"]),
        ServiceBusFullyQualifiedNamespace = Trimmed(config["ServiceBusConnection:fullyQualifiedNamespace"]),

        // AzureWebJobsStorage is a connection string only in local development.
        // Deployed, it is deleted outright (SPEC.md 9.2.1) and the account name
        // below drives identity-based access instead.
        StorageConnectionString = Trimmed(config["AzureWebJobsStorage"]),
        StorageAccountName = Trimmed(config["STORAGE_ACCOUNT_NAME"]),

        QueueName = Trimmed(config["ORDERS_QUEUE"]) ?? "orders",
        TopicName = Trimmed(config["ORDER_EVENTS_TOPIC"]) ?? "order-events",
        OrdersTable = Trimmed(config["TABLE_ORDERS"]) ?? "Orders",
        AuditTable = Trimmed(config["TABLE_AUDIT"]) ?? "AuditLog",
        ProcessingDelayMs = int.TryParse(config["PROCESSING_DELAY_MS"], out var delay) ? delay : 250
    };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
