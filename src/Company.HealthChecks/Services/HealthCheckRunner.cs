using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.HealthChecks.Services;

/// <summary>
/// Default implementation of <see cref="IHealthCheckRunner"/>.
/// Delegates to <see cref="HealthCheckService"/>, which is the built-in ASP.NET Core
/// service responsible for executing all registered <see cref="IHealthCheck"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HealthCheckService"/> is registered automatically by
/// <c>IServiceCollection.AddHealthChecks()</c> and resolves checks from the DI container.
/// This class is intentionally thin — it exists only to provide a mockable interface
/// boundary (<see cref="IHealthCheckRunner"/>) for testing, without coupling your
/// application code directly to <see cref="HealthCheckService"/>.
/// </para>
/// <para>
/// Microsoft documentation:
/// <list type="bullet">
///   <item>HealthCheckService — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice</item>
///   <item>HealthCheckService.CheckHealthAsync — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice.checkhealthasync</item>
///   <item>HealthCheckRegistration — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckregistration</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class HealthCheckRunner : IHealthCheckRunner
{
    private readonly HealthCheckService _healthCheckService;

    public HealthCheckRunner(HealthCheckService healthCheckService)
        => _healthCheckService = healthCheckService;

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="HealthCheckService.CheckHealthAsync(CancellationToken)"/> with no predicate,
    /// which runs every registered check. Reference:
    /// https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice.checkhealthasync
    /// </remarks>
    public Task<HealthReport> RunAllAsync(CancellationToken cancellationToken = default)
        => _healthCheckService.CheckHealthAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="HealthCheckService.CheckHealthAsync(Func{HealthCheckRegistration, bool}, CancellationToken)"/>
    /// with a predicate that filters by <see cref="HealthCheckRegistration.Tags"/>.
    /// Only checks whose tag set contains <paramref name="tag"/> are executed.
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice.checkhealthasync
    /// </remarks>
    public Task<HealthReport> RunByTagAsync(string tag, CancellationToken cancellationToken = default)
        => _healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains(tag),
            cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="HealthCheckService.CheckHealthAsync(Func{HealthCheckRegistration, bool}, CancellationToken)"/>
    /// with a predicate that matches <see cref="HealthCheckRegistration.Name"/> (case-insensitive).
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice.checkhealthasync
    /// </remarks>
    public Task<HealthReport> RunByNameAsync(string checkName, CancellationToken cancellationToken = default)
        => _healthCheckService.CheckHealthAsync(
            registration => registration.Name.Equals(checkName, StringComparison.OrdinalIgnoreCase),
            cancellationToken);
}
