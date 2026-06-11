// ┌─────────────────────────────────────────────────────────────────────────┐
// │  SampleWebApi — shows how any microservice wires up Company.HealthChecks │
// │                                                                           │
// │  Health checks run ON DEMAND only.                                        │
// │  Nothing polls in the background. Checks fire when:                       │
// │    (a) a /health/* endpoint is hit                                        │
// │    (b) IHealthCheckRunner is called from your own code                    │
// └─────────────────────────────────────────────────────────────────────────┘

using Company.HealthChecks.Extensions;
using Company.HealthChecks.Models;
using Company.HealthChecks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ── Step 1: Register health checks ───────────────────────────────────────────
//
// Two lines per service. Change ServiceName, DatabaseType, and connection
// strings. Everything else is handled by the shared package.
//
builder.Services.AddServiceHealthChecks(o =>
{
    o.ServiceName              = "OrderService";
    o.DatabaseType             = DatabaseType.SqlServer;
    o.DatabaseConnectionString = builder.Configuration.GetConnectionString("Default")
                                    ?? "Server=localhost;Database=OrderDb;Integrated Security=true;";

    // Optional: uncomment if this service uses Redis
    // o.RedisConnectionString = builder.Configuration["Redis:ConnectionString"];

    // Optional: override default endpoint paths
    // o.LivenessPath  = "/health/live";   // default
    // o.ReadinessPath = "/health/ready";  // default
    // o.DetailPath    = "/health/detail"; // default
});

var app = builder.Build();

app.MapControllers();

// ── Step 2: Map the three standard endpoints ──────────────────────────────────
//
//   GET /health/live   → plain text "Healthy" — for Docker / K8s liveness probe
//   GET /health/ready  → JSON — for K8s readiness probe (checks DB connectivity)
//   GET /health/detail → full JSON report — for developers and monitoring
//
app.MapServiceHealthChecks();

// ── Step 3 (optional): on-demand check from your own code ────────────────────
//
// Inject IHealthCheckRunner anywhere you need a programmatic health check.
// Example: an admin endpoint that a developer can curl on demand.
//
app.MapGet("/admin/healthcheck", async (
    [FromServices] IHealthCheckRunner runner,
    CancellationToken ct) =>
{
    // Runs ALL checks right now and returns the full report.
    var report = await runner.RunAllAsync(ct);

    var result = new
    {
        overallStatus = report.Status.ToString(),
        durationMs    = report.TotalDuration.TotalMilliseconds,
        checks        = report.Entries.Select(e => new
        {
            name     = e.Key,
            status   = e.Value.Status.ToString(),
            duration = e.Value.Duration.TotalMilliseconds,
            error    = e.Value.Exception?.Message,
        }),
    };

    return report.Status == HealthStatus.Unhealthy
        ? Results.Json(result, statusCode: 503)
        : Results.Ok(result);
});

// ── Example: check ONLY the database (by tag) ────────────────────────────────
app.MapGet("/admin/healthcheck/db", async (
    [FromServices] IHealthCheckRunner runner,
    CancellationToken ct) =>
{
    // "ready" tag = all dependency checks (DB, Redis, etc.)
    var report = await runner.RunByTagAsync("ready", ct);
    return report.Status == HealthStatus.Unhealthy
        ? Results.Problem("Database unreachable", statusCode: 503)
        : Results.Ok(new { status = report.Status.ToString() });
});

// ── Example: inject runner into a service class ───────────────────────────────
//
//   public class OrderProcessor(IHealthCheckRunner health)
//   {
//       public async Task ProcessAsync(Order order, CancellationToken ct)
//       {
//           var report = await health.RunByTagAsync("ready", ct);
//           if (report.Status == HealthStatus.Unhealthy)
//               throw new InvalidOperationException("Cannot process — DB is down.");
//
//           // ... normal processing
//       }
//   }

app.Run();
