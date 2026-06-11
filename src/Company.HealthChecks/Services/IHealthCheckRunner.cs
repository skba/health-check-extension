using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.HealthChecks.Services;

/// <summary>
/// Runs health checks on demand — inject this wherever you need a programmatic check.
/// No background polling; checks execute only when you call these methods.
/// </summary>
/// <remarks>
/// <para>
/// This interface wraps <see cref="HealthCheckService"/>, which is the core ASP.NET Core
/// service that executes registered <see cref="IHealthCheck"/> implementations.
/// By injecting <see cref="IHealthCheckRunner"/> you can trigger the same checks that
/// the HTTP endpoints use, from any class in your application.
/// </para>
/// <para>
/// Microsoft documentation:
/// <list type="bullet">
///   <item>HealthCheckService API — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice</item>
///   <item>IHealthCheck interface — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheck</item>
///   <item>HealthReport — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreport</item>
///   <item>Health checks in ASP.NET Core — https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Inject into a minimal API endpoint:
/// <code>
/// app.MapGet("/admin/check", async (IHealthCheckRunner runner) =>
/// {
///     var report = await runner.RunAllAsync();
///     return report.Status == HealthStatus.Healthy
///         ? Results.Ok(report)
///         : Results.StatusCode(503);
/// });
/// </code>
/// Inject into a service class:
/// <code>
/// public class OrderProcessor(IHealthCheckRunner health)
/// {
///     public async Task ProcessAsync(Order order, CancellationToken ct)
///     {
///         var report = await health.RunByTagAsync("ready", ct);
///         if (report.Status == HealthStatus.Unhealthy)
///             throw new InvalidOperationException("Cannot process — database is unavailable.");
///         // ... proceed with normal processing
///     }
/// }
/// </code>
/// </example>
public interface IHealthCheckRunner
{
    /// <summary>
    /// Runs every registered health check and returns the combined <see cref="HealthReport"/>.
    /// Equivalent to hitting <c>/health/detail</c> but callable from any class.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="HealthReport"/> containing the result of every registered check.
    /// See https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreport
    /// </returns>
    Task<HealthReport> RunAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs only checks that carry the specified tag.
    /// </summary>
    /// <param name="tag">
    /// <list type="bullet">
    ///   <item><c>"live"</c> — liveness checks only (process self-check, no I/O).</item>
    ///   <item><c>"ready"</c> — readiness checks only (database, Redis, external deps).</item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// Tags are assigned during registration in <see cref="Company.HealthChecks.Extensions.HealthCheckExtensions.AddServiceHealthChecks"/>.
    /// See "Filter health checks" in the Microsoft docs:
    /// https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#filter-health-checks
    /// </remarks>
    Task<HealthReport> RunByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs only the check registered under the given name.
    /// Name format is <c>"{ServiceName}-{provider}"</c>,
    /// e.g. <c>"OrderService-sqlserver"</c> or <c>"OrderService-redis"</c>.
    /// </summary>
    /// <param name="checkName">The exact name the check was registered under.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<HealthReport> RunByNameAsync(string checkName, CancellationToken cancellationToken = default);
}
