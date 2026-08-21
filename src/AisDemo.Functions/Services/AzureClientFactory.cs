using Azure.Core;
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
    /// <summary>
    /// Builds the credential used for all identity-based access.
    /// </summary>
    /// <remarks>
    /// Managed identity is excluded when not running in Azure. A bare
    /// <c>DefaultAzureCredential</c> tries ManagedIdentityCredential before
    /// AzureCliCredential, and off-Azure that means probing the instance
    /// metadata endpoint at 169.254.169.254 — an address that simply is not
    /// routable outside Azure. Measured cost on a developer machine: roughly
    /// 25 seconds of retries, and then a failure rather than a fall-through to
    /// the developer's own az login.
    ///
    /// WEBSITE_INSTANCE_ID is injected by the Functions platform, so its
    /// absence is a reliable "not running in Azure" signal.
    /// </remarks>
    private static TokenCredential CreateCredential()
    {
        var runningInAzure = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = !runningInAzure,

            // Never block a Functions host waiting on a browser prompt.
            ExcludeInteractiveBrowserCredential = true
        });
    }

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
                CreateCredential());
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
            return new TableServiceClient(endpoint, CreateCredential());
        }

        throw new InvalidOperationException(
            "No storage configuration found. Set AzureWebJobsStorage for local " +
            "development, or STORAGE_ACCOUNT_NAME when deployed.");
    }
}
