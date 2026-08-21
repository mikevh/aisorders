using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AisDemo.Functions.Services;

/// <summary>
/// Creates the demo tables when running against Azurite.
/// </summary>
/// <remarks>
/// Terraform creates them in Azure, so this is a no-op there and is skipped
/// entirely rather than paying a cold-start cost on every deployed instance.
/// Azurite starts empty, so the local path genuinely needs it.
/// </remarks>
public sealed class TableBootstrapper : IHostedService
{
    private readonly DemoOptions _options;
    private readonly OrderRepository _orders;
    private readonly AuditRepository _audit;
    private readonly ILogger<TableBootstrapper> _logger;

    public TableBootstrapper(
        DemoOptions options,
        OrderRepository orders,
        AuditRepository audit,
        ILogger<TableBootstrapper> logger)
    {
        _options = options;
        _orders = orders;
        _audit = audit;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.UsesLocalEmulators)
        {
            return;
        }

        _logger.LogInformation("Local emulators detected; ensuring demo tables exist");
        await _orders.EnsureExistsAsync(cancellationToken);
        await _audit.EnsureExistsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
