# Company.HealthChecks

Shared, zero-duplication health check infrastructure for all Company microservices built on .NET 10.

- **On-demand only** — checks run when an endpoint is hit or `IHealthCheckRunner` is called. No background polling.
- **Two lines per service** — `AddServiceHealthChecks(...)` + `MapServiceHealthChecks()`.
- **Three standard endpoints** — `/health/live`, `/health/ready`, `/health/detail` — identical schema across every service.
- **Programmatic runner** — inject `IHealthCheckRunner` to trigger checks from your own code at any time.
- **Supports** SQL Server, Azure SQL Database, CosmosDB, Redis.

---

## Solution structure

```
Company.HealthChecks.sln
│
├── src/
│   └── Company.HealthChecks/          ← shared library (published as NuGet package)
│       ├── Models/
│       │   └── ServiceHealthCheckOptions.cs
│       ├── Extensions/
│       │   └── HealthCheckExtensions.cs
│       ├── Services/
│       │   ├── IHealthCheckRunner.cs
│       │   └── HealthCheckRunner.cs
│       └── Writers/
│           └── JsonHealthCheckResponseWriter.cs
│
└── samples/
    └── SampleWebApi/                  ← how a microservice consumes the package
        └── Program.cs
```

---

## Understanding the health endpoints

This library registers three HTTP endpoints, each serving a distinct purpose. Understanding
the difference is important for correct Docker and Kubernetes integration.

### `/health/live` — Liveness

**What it checks:** whether the process itself is alive and responding to requests.
No database connections, no network calls, no I/O of any kind.

**What it means:** "Is this container running and not deadlocked?"

**When it returns 503:** only if the process is in a state where it cannot answer HTTP
requests at all (e.g., deadlock, OOM, crash). Under normal operation this endpoint
always returns 200.

**Response format:** plain text — `Healthy` or `Unhealthy`.

**Use for:**
- Docker `HEALTHCHECK` directive — Docker monitors this to decide whether to restart
  a container.
- Kubernetes `livenessProbe` — if this returns a non-2xx status, Kubernetes restarts
  the pod. **Do not put database checks here.** A slow database would cause unnecessary
  pod restarts.

**Example response:**
```
Healthy
```

---

### `/health/ready` — Readiness

**What it checks:** whether the service's external dependencies (database, Redis) are
reachable and accepting connections. Runs the configured SQL probe query (default `SELECT 1`)
against the database.

**What it means:** "Is this service ready to handle real user traffic?"

**When it returns 503:** when any dependency check reports `Unhealthy` (e.g., cannot reach
the database). A `Degraded` result (e.g., Redis is slow but available) still returns 200.

**Response format:** JSON (see schema below).

**Use for:**
- Kubernetes `readinessProbe` — if this returns 503, Kubernetes removes the pod from the
  Service load balancer so no new requests are routed to it. The pod is **not restarted**.
  Once the database recovers and the probe passes again, the pod is added back automatically.
- Health dashboards that need to know if all service dependencies are available.

**Key distinction from liveness:** a failing database should make the service *unready*
(stop receiving traffic) but should not make it *not alive* (trigger a restart). If you
put the database check on the liveness probe, every database blip causes a pod restart,
which makes recovery slower.

**Example response:**
```json
{
  "status": "Healthy",
  "durationMs": 14.2,
  "timestamp": "2025-06-08T10:30:00Z",
  "checks": [
    {
      "name": "OrderService-sqlserver",
      "status": "Healthy",
      "durationMs": 11.4
    }
  ]
}
```

---

### `/health/detail` — Detail

**What it checks:** every registered check — liveness, database, Redis, and any custom
checks added by the service — in one call.

**What it means:** "Give me a complete picture of this service's health right now."

**Response format:** full JSON report with per-check timing, status, and error messages.

**Use for:**
- Developer investigation during incidents — hit this endpoint to immediately see which
  component is failing and how long each check took.
- CI pipeline validation — assert all checks are `Healthy` after a deployment.
- Monitoring dashboards (Azure Monitor, Grafana) — scrape this endpoint to build
  per-service health panels.
- On-call runbooks — the first URL to check when an alert fires.

**Important:** this endpoint exposes internal service topology (database hostnames, error
messages). Restrict it to internal networks or protect it with an authentication policy
in production.

**Example response:**
```json
{
  "status": "Degraded",
  "durationMs": 28.7,
  "timestamp": "2025-06-08T10:30:00Z",
  "checks": [
    {
      "name": "OrderService-self",
      "status": "Healthy",
      "durationMs": 0.1
    },
    {
      "name": "OrderService-sqlserver",
      "status": "Healthy",
      "durationMs": 18.3
    },
    {
      "name": "OrderService-redis",
      "status": "Degraded",
      "durationMs": 10.2,
      "error": "Connection refused"
    }
  ]
}
```

---

### Status values and HTTP codes

| `HealthStatus` | HTTP status | Meaning |
|---|---|---|
| `Healthy` | 200 OK | All checks passed |
| `Degraded` | 200 OK | Partial failure (e.g. Redis down). Service keeps receiving traffic. |
| `Unhealthy` | 503 Service Unavailable | Critical failure. Kubernetes removes pod from load balancer. |

Microsoft reference — `HealthStatus` enum:
https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthstatus

---

## Quick start — consuming the package

### 1. Install

```xml
<PackageReference Include="Company.HealthChecks" Version="1.0.0" />
```

### 2. Register in `Program.cs`

```csharp
builder.Services.AddServiceHealthChecks(o =>
{
    o.ServiceName              = "OrderService";
    o.DatabaseType             = DatabaseType.SqlServer;
    o.DatabaseConnectionString = builder.Configuration.GetConnectionString("Default");

    // Optional — only set if this service uses Redis
    // o.RedisConnectionString = builder.Configuration["Redis:ConnectionString"];
});
```

### 3. Map endpoints

```csharp
var app = builder.Build();
app.MapServiceHealthChecks();
app.Run();
```

### 4. On-demand programmatic check

Inject `IHealthCheckRunner` anywhere in your application:

```csharp
// In a minimal API endpoint
app.MapGet("/admin/healthcheck", async (IHealthCheckRunner runner, CancellationToken ct) =>
{
    var report = await runner.RunAllAsync(ct);
    return report.Status == HealthStatus.Unhealthy
        ? Results.StatusCode(503)
        : Results.Ok(report);
});

// In a service class — guard before doing expensive work
public class OrderProcessor(IHealthCheckRunner health)
{
    public async Task ProcessAsync(Order order, CancellationToken ct)
    {
        var report = await health.RunByTagAsync("ready", ct); // DB only
        if (report.Status == HealthStatus.Unhealthy)
            throw new InvalidOperationException("Database is unavailable.");
        // ... proceed
    }
}
```

`IHealthCheckRunner` methods:

| Method | Runs |
|---|---|
| `RunAllAsync()` | Every registered check |
| `RunByTagAsync("ready")` | Dependency checks (DB, Redis) |
| `RunByTagAsync("live")` | Liveness self-check only |
| `RunByNameAsync("OrderService-sqlserver")` | One specific check by name |

---

## Docker integration

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Hits /health/live every 30s. No DB call — just proves the process is up.
# If it fails 3 times, Docker marks the container unhealthy and restarts it.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "YourService.dll"]
```

Docker `HEALTHCHECK` reference:
https://docs.docker.com/engine/reference/builder/#healthcheck

---

## Kubernetes integration

```yaml
livenessProbe:
  # Restarts the pod if the process stops responding.
  # Uses /health/live — no database call, so DB failures do not trigger restarts.
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 20
  failureThreshold: 3

readinessProbe:
  # Removes the pod from the Service load balancer if dependencies are unreachable.
  # Uses /health/ready — checks database and Redis.
  # Pod is NOT restarted; it re-enters rotation automatically when checks pass again.
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 10
  failureThreshold: 3

startupProbe:
  # Gives slow-starting pods (e.g. EF Core migrations on boot) up to 5 minutes
  # before the livenessProbe starts counting failures.
  httpGet:
    path: /health/live
    port: 8080
  failureThreshold: 30
  periodSeconds: 10
```

Kubernetes probe documentation:
https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/

---

## Publishing

### Option A — nuget.org (public)

```bash
# 1. Build in Release
dotnet build src/Company.HealthChecks/Company.HealthChecks.csproj -c Release

# 2. Pack — produces bin/Release/Company.HealthChecks.1.0.0.nupkg
dotnet pack src/Company.HealthChecks/Company.HealthChecks.csproj \
    -c Release \
    --no-build \
    -o ./nupkg

# 3. Push — get your API key from https://www.nuget.org/account/apikeys
dotnet nuget push ./nupkg/Company.HealthChecks.1.0.0.nupkg \
    --api-key YOUR_NUGET_API_KEY \
    --source https://api.nuget.org/v3/index.json
```

NuGet CLI reference: https://learn.microsoft.com/en-us/nuget/reference/nuget-exe-cli-reference

---

### Option B — Azure Artifacts (private, recommended for internal packages)

#### One-time feed setup

1. In Azure DevOps, go to **Artifacts → Create feed**.
   - Name: `company-internal`
   - Visibility: **Private**
   - Enable **"Include packages from common public sources"** to also proxy nuget.org.

2. Add the feed to each developer machine (run once):

```bash
# Replace <ORG> and <FEED> with your Azure DevOps organisation and feed name
dotnet nuget add source \
    "https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/nuget/v3/index.json" \
    --name "company-internal" \
    --username az \
    --password YOUR_PAT_TOKEN
```

The PAT requires **Packaging: Read & Write** scope.
Azure Artifacts PAT guide: https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/nuget-exe?view=azure-devops#publish-packages

#### Publish the package

```bash
dotnet pack src/Company.HealthChecks/Company.HealthChecks.csproj -c Release -o ./nupkg

dotnet nuget push ./nupkg/Company.HealthChecks.1.0.0.nupkg \
    --source "company-internal" \
    --api-key az   # Azure Artifacts ignores the value but requires the flag
```

Azure Artifacts NuGet publish reference:
https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/publish?view=azure-devops

#### Consume in a microservice — add `nuget.config` at repo root

This ensures both local dev and CI resolve the private feed without per-machine setup:

```xml
<!-- nuget.config — commit this to source control -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org"        value="https://api.nuget.org/v3/index.json" />
    <add key="company-internal" value="https://pkgs.dev.azure.com/<ORG>/_packaging/<FEED>/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

Then in each microservice `.csproj`:

```xml
<PackageReference Include="Company.HealthChecks" Version="1.0.0" />
```

nuget.config reference: https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file

---

### Option C — Azure Pipelines CI/CD publish

Add this stage to `azure-pipelines.yml` so every merge to `main` publishes automatically:

```yaml
trigger:
  branches:
    include: [main]

pool:
  vmImage: ubuntu-latest

variables:
  buildConfiguration: Release
  feedName: company-internal

stages:
  - stage: Pack_and_Publish
    jobs:
      - job: Publish
        steps:
          - task: UseDotNet@2
            inputs:
              version: '10.x'

          - script: |
              dotnet build src/Company.HealthChecks/Company.HealthChecks.csproj \
                -c $(buildConfiguration)
            displayName: Build

          - script: |
              dotnet pack src/Company.HealthChecks/Company.HealthChecks.csproj \
                -c $(buildConfiguration) \
                --no-build \
                -o $(Build.ArtifactStagingDirectory)/nupkg
            displayName: Pack

          - task: NuGetAuthenticate@1
            displayName: Authenticate with Azure Artifacts

          - task: NuGetCommand@2
            displayName: Push to Azure Artifacts
            inputs:
              command: push
              packagesToPush: '$(Build.ArtifactStagingDirectory)/nupkg/*.nupkg'
              nuGetFeedType: internal
              publishVstsFeed: $(feedName)
```

Azure Pipelines NuGet task reference:
https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/nuget-command-v2

> **Versioning tip:** use [MinVer](https://github.com/adamralph/minver) or
> [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) to
> auto-increment `<Version>` from git tags instead of editing the `.csproj` manually.

---

## Microsoft documentation references

All ASP.NET Core APIs and concepts used in this library:

| Topic | Link |
|---|---|
| Health checks in ASP.NET Core (overview) | https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks |
| `AddHealthChecks` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.healthcheckservicecollectionextensions.addhealthchecks |
| `MapHealthChecks` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.healthcheckendpointroutebuilderextensions.maphealthchecks |
| `HealthCheckOptions` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions |
| `HealthCheckService` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice |
| `HealthCheckService.CheckHealthAsync` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckservice.checkhealthasync |
| `IHealthCheck` interface | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheck |
| `HealthReport` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreport |
| `HealthReportEntry` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthreportentry |
| `HealthStatus` enum | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthstatus |
| `HealthCheckRegistration` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.healthcheckregistration |
| `HealthCheckOptions.ResultStatusCodes` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions.resultstatuscodes |
| `HealthCheckOptions.ResponseWriter` | https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.healthchecks.healthcheckoptions.responsewriter |
| Filter health checks by tag | https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#filter-health-checks |
| Separate readiness and liveness probes | https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#separate-readiness-and-liveness-probes |
| Customize output (ResponseWriter) | https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#customize-output |
| `IHealthCheckPublisher` (polling — not used here) | https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.diagnostics.healthchecks.ihealthcheckpublisher |
| Azure Cosmos DB .NET SDK v3 | https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-dotnet-v3 |
| Azure Cache for Redis overview | https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-overview |
| Azure SQL — connect with .NET | https://learn.microsoft.com/en-us/azure/azure-sql/database/connect-query-dotnet-core |
| NuGet CLI reference | https://learn.microsoft.com/en-us/nuget/reference/nuget-exe-cli-reference |
| Azure Artifacts — publish NuGet | https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/publish |
| Azure Pipelines — NuGet task | https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/nuget-command-v2 |
| nuget.config file reference | https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file |

---

## Options reference

| Property | Type | Default | Description |
|---|---|---|---|
| `ServiceName` | `string` | `"UnnamedService"` | Name shown in all health report entries |
| `DatabaseType` | `DatabaseType` | `None` | `SqlServer`, `CosmosDb`, or `None` |
| `DatabaseConnectionString` | `string?` | `null` | Primary database connection string |
| `DatabaseProbeQuery` | `string` | `"SELECT 1"` | SQL used to probe the database |
| `CosmosDbConnectionString` | `string?` | `null` | Cosmos DB connection string |
| `CosmosDbDatabaseName` | `string?` | `null` | Cosmos DB database name |
| `RedisConnectionString` | `string?` | `null` | Redis connection string (optional) |
| `LivenessPath` | `string` | `"/health/live"` | Liveness endpoint path |
| `ReadinessPath` | `string` | `"/health/ready"` | Readiness endpoint path |
| `DetailPath` | `string` | `"/health/detail"` | Detail report endpoint path |
