using Company.HealthChecks.Checks;
using Company.HealthChecks.Models;
using Company.HealthChecks.Services;
using Company.HealthChecks.Writers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.HealthChecks.Extensions;

/// <summary>
/// Extension methods that wire up health checks in any ASP.NET Core microservice
/// with two method calls in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// This class uses the standard ASP.NET Core health check pipeline:
/// <list type="bullet">
///   <item><see cref="HealthCheckServiceCollectionExtensions.AddHealthChecks"/> registers the framework — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.healthcheckservicecollectionextensions.addhealthchecks</item>
///   <item><see cref="HealthCheckEndpointRouteBuilderExtensions.MapHealthChecks"/> maps HTTP endpoints — https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.healthcheckendpointroutebuilderextensions.maphealthchecks</item>
///   <item><see cref="HealthCheckOptions"/> configures per-endpoint filtering and response writing — https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions</item>
/// </list>
/// </para>
/// <para>
/// Microsoft documentation — health checks overview:
/// https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks
/// </para>
/// </remarks>
public static class HealthCheckExtensions
{
    // Tags are the mechanism ASP.NET Core uses to route checks to specific endpoints.
    // A check tagged "live" runs only on the liveness endpoint; "ready" only on readiness.
    // Reference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#filter-health-checks

    internal const string TagLive  = "live";
    internal const string TagReady = "ready";

    // ─────────────────────────────────────────────────────────────────────────
    // DI Registration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all health checks for this microservice and the
    /// <see cref="IHealthCheckRunner"/> for programmatic on-demand execution.
    /// Call once in <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On-demand execution only.</b> Checks run when an HTTP endpoint is hit
    /// or <see cref="IHealthCheckRunner"/> is called explicitly. There is no
    /// background timer or publisher registered — see
    /// <see cref="IHealthCheckPublisherHostedService"/> if you need polling:
    /// https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheckpublisher
    /// </para>
    /// <para>
    /// The following community packages are used for database probes.
    /// Each is a thin wrapper around the vendor SDK that implements <see cref="IHealthCheck"/>:
    /// <list type="bullet">
    ///   <item>SQL Server — <c>AspNetCore.HealthChecks.SqlServer</c></item>
    ///   <item>Cosmos DB — <c>AspNetCore.HealthChecks.CosmosDb</c></item>
    ///   <item>Redis — <c>AspNetCore.HealthChecks.Redis</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// IHealthCheck interface reference:
    /// https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheck
    /// </para>
    /// </remarks>
    /// <example>
    /// SQL Server:
    /// <code>
    /// builder.Services.AddServiceHealthChecks(o =>
    /// {
    ///     o.ServiceName              = "OrderService";
    ///     o.DatabaseType             = DatabaseType.SqlServer;
    ///     o.DatabaseConnectionString = builder.Configuration.GetConnectionString("Default");
    /// });
    /// </code>
    /// SQL Server + Redis:
    /// <code>
    /// builder.Services.AddServiceHealthChecks(o =>
    /// {
    ///     o.ServiceName              = "CatalogService";
    ///     o.DatabaseType             = DatabaseType.SqlServer;
    ///     o.DatabaseConnectionString = builder.Configuration.GetConnectionString("Default");
    ///     o.RedisConnectionString    = builder.Configuration["Redis:ConnectionString"];
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddServiceHealthChecks(
        this IServiceCollection services,
        Action<ServiceHealthCheckOptions> configure)
    {
        var options = new ServiceHealthCheckOptions();
        configure(options);

        // Store so MapServiceHealthChecks can read the configured endpoint paths
        services.AddSingleton(options);

        // Register the on-demand programmatic runner
        // Scoped lifetime matches HealthCheckService's own lifetime
        services.AddScoped<IHealthCheckRunner, HealthCheckRunner>();

        // AddHealthChecks registers HealthCheckService and the check host service.
        // Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.healthcheckservicecollectionextensions.addhealthchecks
        var builder = services.AddHealthChecks();

        RegisterLivenessCheck(builder, options);
        RegisterDatabaseCheck(builder, options);
        RegisterRedisCheck(builder, options);

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Endpoint mapping
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps the three standard health check HTTP endpoints.
    /// Call after <c>var app = builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <c>MapHealthChecks</c> from the ASP.NET Core routing system to bind
    /// each path to a filtered subset of the registered checks.
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.healthcheckendpointroutebuilderextensions.maphealthchecks
    /// </para>
    /// <para>
    /// HTTP status codes returned per <see cref="HealthStatus"/>:
    /// <list type="bullet">
    ///   <item><see cref="HealthStatus.Healthy"/> → 200 OK</item>
    ///   <item><see cref="HealthStatus.Degraded"/> → 200 OK (still routes traffic)</item>
    ///   <item><see cref="HealthStatus.Unhealthy"/> → 503 Service Unavailable</item>
    /// </list>
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions.resultstatuscodes
    /// </para>
    /// <para>
    /// Endpoint summary:
    /// <list type="table">
    ///   <listheader><term>Path</term><description>Purpose</description></listheader>
    ///   <item>
    ///     <term>/health/live</term>
    ///     <description>Liveness: is the process alive? No I/O. Plain-text response. Docker / K8s livenessProbe.</description>
    ///   </item>
    ///   <item>
    ///     <term>/health/ready</term>
    ///     <description>Readiness: are dependencies reachable? JSON. K8s readinessProbe.</description>
    ///   </item>
    ///   <item>
    ///     <term>/health/detail</term>
    ///     <description>Detail: all checks, full JSON report. For developers and dashboards.</description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    /// app.MapServiceHealthChecks();
    /// app.Run();
    /// </code>
    /// </example>
    public static WebApplication MapServiceHealthChecks(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ServiceHealthCheckOptions>();

        // ── /health/live — Liveness ───────────────────────────────────────────
        // Runs only checks tagged "live" (the self-check registered above).
        // No database call. Returns plain text "Healthy" or "Unhealthy".
        // Used by Docker HEALTHCHECK and Kubernetes livenessProbe.
        // If this returns 503, the container/pod is restarted.
        // Reference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#separate-readiness-and-liveness-probes
        app.MapHealthChecks(options.LivenessPath, new HealthCheckOptions
        {
            Predicate         = r => r.Tags.Contains(TagLive),
            ResponseWriter    = WritePlainText,
            ResultStatusCodes = StandardStatusCodes(),
        });

        // ── /health/ready — Readiness ─────────────────────────────────────────
        // Runs only checks tagged "ready" (database, Redis).
        // Returns JSON via JsonHealthCheckResponseWriter.
        // Used by Kubernetes readinessProbe.
        // If this returns 503, the pod is removed from the Service load balancer
        // but NOT restarted — traffic stops, the process keeps running.
        // Reference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#separate-readiness-and-liveness-probes
        app.MapHealthChecks(options.ReadinessPath, new HealthCheckOptions
        {
            Predicate         = r => r.Tags.Contains(TagReady),
            ResponseWriter    = JsonHealthCheckResponseWriter.WriteResponse,
            ResultStatusCodes = StandardStatusCodes(),
        });

        // ── /health/detail — Detail ───────────────────────────────────────────
        // Runs every registered check (no predicate filter).
        // Returns a full JSON report including per-check timing and errors.
        // Intended for developer inspection, CI pipelines, and monitoring dashboards.
        // Restrict this path to internal networks or behind an auth policy in production.
        // Reference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#filter-health-checks
        app.MapHealthChecks(options.DetailPath, new HealthCheckOptions
        {
            Predicate         = _ => true,
            ResponseWriter    = JsonHealthCheckResponseWriter.WriteResponse,
            ResultStatusCodes = StandardStatusCodes(),
        });

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void RegisterLivenessCheck(IHealthChecksBuilder builder, ServiceHealthCheckOptions o)
    {
        // AddCheck registers an inline IHealthCheck implementation via a delegate.
        // Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.healthchecksbuilderaddcheckextensions.addcheck
        builder.AddCheck(
            name:  $"{o.ServiceName}-self",
            check: () => HealthCheckResult.Healthy("Process is running."),
            tags:  [TagLive]);
    }

    private static void RegisterDatabaseCheck(IHealthChecksBuilder builder, ServiceHealthCheckOptions o)
    {
        switch (o.DatabaseType)
        {
            case DatabaseType.SqlServer when o.DatabaseConnectionString is not null:
                // AspNetCore.HealthChecks.SqlServer executes the probe query
                // against the connection string and reports Unhealthy on failure.
                // Package source: https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks
                builder.AddSqlServer(
                    connectionString: o.DatabaseConnectionString,
                    healthQuery:      o.DatabaseProbeQuery,
                    name:             $"{o.ServiceName}-sqlserver",
                    failureStatus:    HealthStatus.Unhealthy,
                    tags:             [TagReady]);
                break;

            case DatabaseType.CosmosDb when o.CosmosDbConnectionString is not null:
                // Uses the custom CosmosDbHealthCheck which wraps CosmosClient directly.
                // CosmosClient is cached inside CosmosDbHealthCheck — one instance per connection string.
                // Reference: https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/best-practice-dotnet#reuse-cosmosclient-instances
                builder.AddCheck(
                    name:          $"{o.ServiceName}-cosmosdb",
                    instance:      new CosmosDbHealthCheck(
                                       o.CosmosDbConnectionString,
                                       o.CosmosDbDatabaseName),
                    failureStatus: HealthStatus.Unhealthy,
                    tags:          [TagReady]);
                break;
        }
    }

    private static void RegisterRedisCheck(IHealthChecksBuilder builder, ServiceHealthCheckOptions o)
    {
        if (o.RedisConnectionString is null) return;

        // Redis failure is Degraded (not Unhealthy) — the service continues routing
        // traffic while the cache is unavailable, but operators are alerted.
        // HealthStatus reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthstatus
        builder.AddRedis(
            redisConnectionString: o.RedisConnectionString,
            name:          $"{o.ServiceName}-redis",
            failureStatus: HealthStatus.Degraded,
            tags:          [TagReady]);
    }

    /// <summary>
    /// Maps each <see cref="HealthStatus"/> to an HTTP status code.
    /// Degraded returns 200 so the service keeps receiving traffic during partial failures.
    /// </summary>
    /// <remarks>
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions.resultstatuscodes
    /// </remarks>
    private static Dictionary<HealthStatus, int> StandardStatusCodes() => new()
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    };

    private static Task WritePlainText(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        return ctx.Response.WriteAsync(report.Status.ToString());
    }
}
