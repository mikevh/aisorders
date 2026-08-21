using System.Diagnostics;

namespace AisDemo.Functions.Services;

/// <summary>
/// Stamps demo identifiers onto the current span.
/// </summary>
/// <remarks>
/// <para>
/// In OpenTelemetry mode these tags land as custom dimensions on the
/// Application Insights span, which is what makes a single order findable
/// across every stage of its journey with one filter. Structured log arguments
/// alone would attach the identifiers to <c>AppTraces</c> rows only, leaving
/// <c>AppRequests</c> and <c>AppDependencies</c> unfilterable by order.
/// </para>
/// <para>
/// The tag names deliberately match the JSON property names used in the API
/// and message contracts, so the same identifier reads the same way in a
/// payload, a log line, and a KQL query.
/// </para>
/// </remarks>
public static class Telemetry
{
    public static void TagOrder(string? orderId, string? correlationId, string? status = null)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            activity.SetTag("orderId", orderId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            activity.SetTag("correlationId", correlationId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            activity.SetTag("orderStatus", status);
        }
    }

    /// <summary>
    /// Records a handled exception on the current span.
    /// </summary>
    /// <remarks>
    /// Catching an exception and returning a response leaves the span looking
    /// successful, so a failure that the caller sees as a 500 shows up as a
    /// healthy invocation. Recording it here restores the exception to
    /// AppExceptions and marks the span's status as an error, which is what the
    /// Failures blade and the Application Map read.
    /// </remarks>
    public static void RecordHandled(Exception exception)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.AddException(exception);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}
