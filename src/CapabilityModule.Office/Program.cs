using CapabilityModule.Office.Database;
using Npgsql;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register the Postgres data source as a singleton, configured from the
// OFFICE_DB_CONNECTION environment variable. If the variable isn't set,
// the data source is still registered (no crash) but the health check will
// report the database as unavailable — this allows the module to start for
// local dev scenarios where the DB isn't running.
var connectionString = Environment.GetEnvironmentVariable("OFFICE_DB_CONNECTION");
NpgsqlDataSource? dataSource = null;
if (!string.IsNullOrWhiteSpace(connectionString))
{
    dataSource = NpgsqlDataSource.Create(connectionString);
}

if (dataSource is not null)
{
    builder.Services.AddSingleton(dataSource);

    // Apply pending migrations on startup. This runs before the app starts
    // accepting requests, so the schema is guaranteed to be current.
    // Migration scripts are idempotent (CREATE IF NOT EXISTS, etc.).
    try
    {
        await DbInitializer.InitializeAsync(dataSource);
    }
    catch (Exception ex)
    {
        // Log but don't crash — the health check will reflect the DB state,
        // and the module can still serve non-indexing tools.
        Console.Error.WriteLine($"Warning: DB migration failed: {ex.Message}");
    }
}

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless deliberately: VTC connects fresh per tool call rather than
        // holding a session open, so the sidecar can scale or restart
        // independently. Don't flip this without revisiting that architecture
        // decision (see CLAUDE.md).
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp();

// Docker HEALTHCHECK and VTC both use container-running-and-healthy as the entitlement signal
// for this module (see architecture decision: no separate licensing check).
// Now also reflects Postgres connectivity — the /health endpoint checks both the
// process being up and whether the indexing database is reachable.
app.MapGet("/health", async () =>
{
    var dbHealthy = dataSource is not null && await DbInitializer.CheckHealthAsync(dataSource);
    var status = dataSource is null ? "degraded (no DB configured)" :
        dbHealthy ? "healthy" : "degraded (DB unreachable)";

    return Results.Ok(new
    {
        status,
        database = dataSource is null ? "not_configured" : dbHealthy ? "connected" : "unreachable"
    });
});

var manifestJson = await File.ReadAllTextAsync(
    Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
app.MapGet("/manifest", () => Results.Text(manifestJson, "application/json"));

app.Run();
