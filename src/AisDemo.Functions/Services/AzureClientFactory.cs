using Azure.Core;
using Microsoft.Extensions.Logging;
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
    /// <para>
    /// Branching explicitly rather than letting DefaultAzureCredential walk its
    /// chain, because both directions of that chain misbehave here.
    /// </para>
    /// <para>
    /// Off-Azure, ManagedIdentityCredential is tried before AzureCliCredential
    /// and probes the instance metadata endpoint at 169.254.169.254 — not
    /// routable outside Azure — burning roughly 25 seconds on retries before
    /// throwing rather than falling through to the developer's az login.
    /// </para>
    /// <para>
    /// In Azure the opposite failure applies: a Flex Consumption host has no
    /// Azure CLI, no PowerShell, and no environment credentials, so excluding
    /// managed identity leaves the chain with nothing at all.
    /// </para>
    /// <para>
    /// IDENTITY_ENDPOINT is the variable ManagedIdentityCredential itself reads
    /// to locate the token endpoint, so its presence is the definitive signal —
    /// unlike WEBSITE_INSTANCE_ID, which is documented for App Service but is
    /// NOT populated on Flex Consumption. That assumption cost a deploy.
    /// </para>
    /// </remarks>
    private static TokenCredential CreateCredential(ILogger? logger = null)
    {
        var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var msiEndpoint = Environment.GetEnvironmentVariable("MSI_ENDPOINT");
        var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
        var instanceId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");

        var managedIdentityAvailable =
            !string.IsNullOrEmpty(identityEndpoint) || !string.IsNullOrEmpty(msiEndpoint);

        logger?.LogInformation(
            "Credential selection: managedIdentity={ManagedIdentity} IDENTITY_ENDPOINT={HasIdentityEndpoint} " +
            "MSI_ENDPOINT={HasMsiEndpoint} WEBSITE_SITE_NAME={HasSiteName} WEBSITE_INSTANCE_ID={HasInstanceId}",
            managedIdentityAvailable,
            !string.IsNullOrEmpty(identityEndpoint),
            !string.IsNullOrEmpty(msiEndpoint),
            !string.IsNullOrEmpty(siteName),
            !string.IsNullOrEmpty(instanceId));

        if (managedIdentityAvailable)
        {
            return new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = true,
            ExcludeInteractiveBrowserCredential = true
        });
    }

    public static ServiceBusClient CreateServiceBusClient(DemoOptions options, ILogger? logger = null)
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
                CreateCredential(logger));
        }

        throw new InvalidOperationException(
            "No Service Bus configuration found. Set ServiceBusConnection for local " +
            "development, or ServiceBusConnection__fullyQualifiedNamespace when deployed.");
    }

    public static TableServiceClient CreateTableServiceClient(DemoOptions options, ILogger? logger = null)
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
            return new TableServiceClient(endpoint, CreateCredential(logger));
        }

        throw new InvalidOperationException(
            "No storage configuration found. Set AzureWebJobsStorage for local " +
            "development, or STORAGE_ACCOUNT_NAME when deployed.");
    }
}
