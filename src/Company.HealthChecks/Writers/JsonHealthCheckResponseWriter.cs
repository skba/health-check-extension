using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.HealthChecks.Writers;

/// <summary>
/// Writes a structured JSON health report with a consistent schema across every service.
/// Consumers (Azure Monitor, Grafana, custom dashboards) parse one known shape.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core health check endpoints accept a custom
/// <see cref="Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions.ResponseWriter"/>
/// delegate, which defaults to writing a plain-text status string. This class replaces
/// that default with a richer JSON payload that includes per-check timing, status,
/// and any exception message — without exposing full stack traces.
/// </para>
/// <para>
/// Microsoft documentation:
/// <list type="bullet">
///   <item>HealthCheckOptions.ResponseWriter — https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions.responsewriter</item>
///   <item>HealthReport — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreport</item>
///   <item>HealthReportEntry — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreportentry</item>
///   <item>Customize output — https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#customize-output</item>
/// </list>
/// </para>
/// <para>
/// Response shape:
/// <code>
/// {
///   "status": "Healthy",
///   "durationMs": 12.4,
///   "timestamp": "2025-01-15T10:30:00Z",
///   "checks": [
///     {
///       "name": "OrderService-sqlserver",
///       "status": "Healthy",
///       "description": null,
///       "durationMs": 8.1,
///       "error": null
///     }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
public static class JsonHealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,

        // Omit null fields — keeps the response clean for healthy checks
        // Reference: https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonignorewhenwritingnull
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the <see cref="HealthReport"/> as JSON to the HTTP response.
    /// Assign to <see cref="Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions.ResponseWriter"/>.
    /// </summary>
    /// <remarks>
    /// Only <see cref="Exception.Message"/> is serialised — never the full stack trace —
    /// to avoid leaking implementation details in HTTP responses.
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreportentry.exception
    /// </remarks>
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new HealthReportResponse(
            Status:     report.Status.ToString(),
            DurationMs: report.TotalDuration.TotalMilliseconds,
            Timestamp:  DateTimeOffset.UtcNow,
            Checks:     report.Entries.Select(e => new CheckEntry(
                Name:        e.Key,
                Status:      e.Value.Status.ToString(),
                Description: e.Value.Description,
                DurationMs:  e.Value.Duration.TotalMilliseconds,
                Error:       e.Value.Exception?.Message,
                Data:        e.Value.Data.Count > 0
                                 ? e.Value.Data.ToDictionary(k => k.Key, k => k.Value?.ToString())
                                 : null
            )).ToList()
        );

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions));
    }

    // Internal response models — the JSON shape is the public contract, not these types.

    private sealed record HealthReportResponse(
        string                    Status,
        double                    DurationMs,
        DateTimeOffset            Timestamp,
        IReadOnlyList<CheckEntry> Checks);

    private sealed record CheckEntry(
        string                       Name,
        string                       Status,
        string?                      Description,
        double                       DurationMs,
        string?                      Error,
        Dictionary<string, string?>? Data);
}
