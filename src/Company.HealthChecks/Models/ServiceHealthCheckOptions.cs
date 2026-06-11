namespace Company.HealthChecks.Models;

/// <summary>
/// Configuration supplied by each microservice when registering health checks.
/// Only populate the fields relevant to that service's infrastructure.
/// </summary>
/// <remarks>
/// This class is consumed by <see cref="Extensions.HealthCheckExtensions.AddServiceHealthChecks"/>,
/// which maps the values onto the ASP.NET Core health checks builder.
/// <para>
/// Microsoft documentation:
/// <list type="bullet">
///   <item>Health checks in ASP.NET Core — https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks</item>
///   <item>AddHealthChecks API — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.healthcheckservicecollectionextensions.addhealthchecks</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddServiceHealthChecks(o =>
/// {
///     o.ServiceName              = "OrderService";
///     o.DatabaseType             = DatabaseType.SqlServer;
///     o.DatabaseConnectionString = config.GetConnectionString("Default");
/// });
/// </code>
/// </example>
public sealed class ServiceHealthCheckOptions
{
    /// <summary>
    /// Human-readable name shown in every health report entry.
    /// Use the microservice name, e.g. "OrderService", "InventoryService".
    /// </summary>
    public string ServiceName { get; set; } = "UnnamedService";

    // ── Primary database ──────────────────────────────────────────────────────

    /// <summary>Which primary database engine this service uses.</summary>
    public DatabaseType DatabaseType { get; set; } = DatabaseType.None;

    /// <summary>Connection string for the service's primary database.</summary>
    public string? DatabaseConnectionString { get; set; }

    /// <summary>
    /// SQL probe query sent to verify database connectivity.
    /// Defaults to <c>SELECT 1</c>, which works on both SQL Server and Azure SQL.
    /// </summary>
    public string DatabaseProbeQuery { get; set; } = "SELECT 1";

    // ── CosmosDB ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Azure Cosmos DB connection string.
    /// Required when <see cref="DatabaseType"/> is <see cref="DatabaseType.CosmosDb"/>.
    /// </summary>
    /// <remarks>
    /// Microsoft documentation — Azure Cosmos DB:
    /// https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-dotnet-v3
    /// </remarks>
    public string? CosmosDbConnectionString { get; set; }

    /// <summary>Cosmos DB database name to verify is reachable.</summary>
    public string? CosmosDbDatabaseName { get; set; }

    // ── Redis ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redis connection string (e.g. Azure Cache for Redis).
    /// Failure is reported as <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>
    /// — the service continues serving traffic.
    /// Leave <c>null</c> to skip the Redis check.
    /// </summary>
    /// <remarks>
    /// Microsoft documentation — Azure Cache for Redis:
    /// https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-overview
    /// </remarks>
    public string? RedisConnectionString { get; set; }

    // ── Endpoint paths ────────────────────────────────────────────────────────

    /// <summary>
    /// Liveness probe path. Checks only that the process is running — no I/O.
    /// Use for Docker <c>HEALTHCHECK</c> and Kubernetes <c>livenessProbe</c>.
    /// Default: <c>/health/live</c>
    /// </summary>
    /// <remarks>
    /// See "Health checks in ASP.NET Core — Use health checks routing":
    /// https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#use-health-checks-routing
    /// </remarks>
    public string LivenessPath { get; set; } = "/health/live";

    /// <summary>
    /// Readiness probe path. Checks that external dependencies (DB, Redis) are reachable.
    /// Use for Kubernetes <c>readinessProbe</c>.
    /// Default: <c>/health/ready</c>
    /// </summary>
    /// <remarks>
    /// See "Health checks in ASP.NET Core — Separate readiness and liveness probes":
    /// https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#separate-readiness-and-liveness-probes
    /// </remarks>
    public string ReadinessPath { get; set; } = "/health/ready";

    /// <summary>
    /// Detail report path. Runs all checks and returns a full JSON report.
    /// Intended for developers and monitoring dashboards — restrict in production.
    /// Default: <c>/health/detail</c>
    /// </summary>
    /// <remarks>
    /// See "Health checks in ASP.NET Core — Create health checks":
    /// https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#create-health-checks
    /// </remarks>
    public string DetailPath { get; set; } = "/health/detail";
}

/// <summary>
/// Supported primary database types for the health check probe.
/// </summary>
/// <remarks>
/// Microsoft documentation — SQL Server connection strings:
/// https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/connection-string-syntax
/// </remarks>
public enum DatabaseType
{
    /// <summary>No database check registered.</summary>
    None,

    /// <summary>
    /// Microsoft SQL Server or Azure SQL Database.
    /// Uses <c>AspNetCore.HealthChecks.SqlServer</c> under the hood.
    /// Reference: https://learn.microsoft.com/en-us/azure/azure-sql/database/connect-query-dotnet-core
    /// </summary>
    SqlServer,

    /// <summary>
    /// Azure Cosmos DB (NoSQL API).
    /// Uses <c>AspNetCore.HealthChecks.CosmosDb</c> under the hood.
    /// Reference: https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-dotnet-v3
    /// </summary>
    CosmosDb,
}
