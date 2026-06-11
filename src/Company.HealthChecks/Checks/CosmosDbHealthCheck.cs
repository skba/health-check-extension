using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.HealthChecks.Checks;

/// <summary>
/// Health check that verifies connectivity to an Azure Cosmos DB account.
/// Implements <see cref="IHealthCheck"/> directly using <see cref="CosmosClient"/>
/// from the official Microsoft Azure Cosmos DB SDK.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CosmosClient"/> is designed to be a long-lived singleton — creating one per
/// request is expensive (TCP connections, caches). This class caches clients by connection
/// string using a <see cref="ConcurrentDictionary{TKey, TValue}"/> so the same client is
/// reused across health check calls.
/// </para>
/// <para>
/// When a <see cref="Models.ServiceHealthCheckOptions.CosmosDbDatabaseName"/> is configured,
/// the check reads the database record to confirm both network reachability and that the
/// specific database exists. Without a database name, it reads the account-level metadata.
/// </para>
/// <para>
/// Microsoft documentation:
/// <list type="bullet">
///   <item>IHealthCheck interface — https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheck</item>
///   <item>CosmosClient — https://learn.microsoft.com/en-us/dotnet/api/microsoft.azure.cosmos.cosmosclient</item>
///   <item>CosmosClient best practices — https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/best-practice-dotnet</item>
///   <item>Azure Cosmos DB .NET SDK v3 — https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-dotnet-v3</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class CosmosDbHealthCheck : IHealthCheck
{
    // CosmosClient is thread-safe and meant to be reused for the lifetime of the application.
    // Keyed by connection string so multiple services with different accounts each get their own.
    // Reference: https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/best-practice-dotnet#reuse-cosmosclient-instances
    private static readonly ConcurrentDictionary<string, CosmosClient> ClientCache = new();

    private readonly string  _connectionString;
    private readonly string? _databaseName;

    /// <param name="connectionString">Azure Cosmos DB connection string.</param>
    /// <param name="databaseName">
    /// Optional database name. When supplied, the check reads the database record to confirm
    /// it exists. When omitted, the check reads the account-level metadata.
    /// </param>
    public CosmosDbHealthCheck(string connectionString, string? databaseName)
    {
        _connectionString = connectionString;
        _databaseName     = databaseName;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// HealthCheckContext.Registration.FailureStatus is set by the caller and controls
    /// whether a failure is Unhealthy or Degraded.
    /// Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckcontext
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = ClientCache.GetOrAdd(
                _connectionString,
                cs => new CosmosClient(cs));

            if (_databaseName is not null)
            {
                // Read the database properties — proves both network access and that
                // the named database exists in this account.
                await client
                    .GetDatabase(_databaseName)
                    .ReadAsync(cancellationToken: cancellationToken);
            }
            else
            {
                // Read account-level metadata — proves network access to the endpoint.
                await client.ReadAccountAsync();
            }

            return HealthCheckResult.Healthy("Azure Cosmos DB is reachable.");
        }
        catch (CosmosException ex)
        {
            // CosmosException carries the HTTP status code from the service.
            // Only expose the status code and message — not the full exception — in the report.
            return new HealthCheckResult(
                status:      context.Registration.FailureStatus,
                description: $"Azure Cosmos DB returned HTTP {(int)ex.StatusCode} ({ex.StatusCode}).",
                exception:   ex);
        }
        catch (Exception ex)
        {
            // Network-level failures (DNS, TLS, timeout) land here.
            return new HealthCheckResult(
                status:      context.Registration.FailureStatus,
                description: "Azure Cosmos DB is unreachable.",
                exception:   ex);
        }
    }
}
