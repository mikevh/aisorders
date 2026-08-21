using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace AisDemo.Functions.Services;

/// <summary>
/// The single place that decides how to authenticate to Azure.
/// </summary>
/// <remarks>
/// <para>
/// SPEC.md 12.1 records the constraint this exists to contain: the Service Bus
/// emulator supports only a fixed local SAS connection string, while the
/// deployed app uses identity-based connections with no secrets at all. Those
/// are genuinely different authentication models, and if the choice were made
/// at each call site it would be scattered through every function.
/// </para>
/// <para>
/// Keep that decision here. If a second file starts branching on whether a
/// connection string is present, this abstraction has failed.
/// </para>
/// </remarks>
public static class AzureClientFactory
{
    public static ServiceBusClient CreateServiceBusClient(DemoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceBusConnectionString))
        {
            // Local: emulator, SAS connection string.
            return new ServiceBusClient(options.ServiceBusConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceBusFullyQualifiedNamespace))
        {
            // Azure: system-assigned managed identity, no secret involved.
            return new ServiceBusClient(
                options.ServiceBusFullyQualifiedNamespace,
                new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "No Service Bus configuration found. Set ServiceBusConnection for local " +
            "development, or ServiceBusConnection__fullyQualifiedNamespace when deployed.");
    }

    public static TableServiceClient CreateTableServiceClient(DemoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.StorageConnectionString))
        {
            // Local: Azurite, via UseDevelopmentStorage=true or an explicit
            // connection string.
            return new TableServiceClient(options.StorageConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.StorageAccountName))
        {
            var endpoint = new Uri($"https://{options.StorageAccountName}.table.core.windows.net");
            return new TableServiceClient(endpoint, new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "No storage configuration found. Set AzureWebJobsStorage for local " +
            "development, or STORAGE_ACCOUNT_NAME when deployed.");
    }
}
