using AisDemo.Functions;
using AisDemo.Functions.Services;
using Azure.Core.Serialization;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Telemetry runs in OpenTelemetry mode (host.json telemetryMode), exporting to
// the Application Insights instance named by APPLICATIONINSIGHTS_CONNECTION_STRING.
// Both the gateway and this app report to the same instance, which is what lets
// one order appear as a single end-to-end transaction (SPEC.md 10).
//
// The exporter is attached only when a connection string is present.
// UseAzureMonitorExporter() throws "A connection string was not found" at
// startup otherwise, which would make the fully-local development path in
// SPEC.md 12 impossible — the host cannot start at all without it.
var telemetry = builder.Services
    .AddOpenTelemetry()
    .UseFunctionsWorkerDefaults();

var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    telemetry.UseAzureMonitorExporter();
}

// Applies to payloads the worker itself binds - a Service Bus trigger taking a
// POCO, for example. It does NOT affect the HTTP responses in this project:
// those are ObjectResult going through ASP.NET Core integration, which
// serializes with its own Web defaults. Verified by flipping the naming policy
// and watching responses stay camelCase either way.
builder.Services.Configure<WorkerOptions>(options =>
{
    options.Serializer = new JsonObjectSerializer(JsonDefaults.Options);
});

// Configuration is read once. AzureClientFactory then resolves either
// connection-string or managed-identity credentials from it — the one place
// that distinction lives (SPEC.md 12.1).
var demoOptions = DemoOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(demoOptions);

// Resolved lazily so the factory can log which credential path it chose. That
// line is the fastest way to diagnose an identity failure — see the remarks on
// AzureClientFactory.CreateCredential.
builder.Services.AddSingleton(sp => AzureClientFactory.CreateServiceBusClient(
    demoOptions, sp.GetRequiredService<ILoggerFactory>().CreateLogger("AzureClientFactory")));
builder.Services.AddSingleton(sp => AzureClientFactory.CreateTableServiceClient(
    demoOptions, sp.GetRequiredService<ILoggerFactory>().CreateLogger("AzureClientFactory")));

builder.Services.AddSingleton<OrderMessaging>();
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddSingleton<AuditRepository>();

// Creates the demo tables when running against Azurite; skipped in Azure,
// where Terraform owns them.
builder.Services.AddHostedService<TableBootstrapper>();

builder.Build().Run();
