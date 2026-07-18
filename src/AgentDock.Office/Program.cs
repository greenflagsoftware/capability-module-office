var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless: VTC connects to this module fresh per tool call rather than holding a
        // session open, which keeps the sidecar simple to scale/restart independently.
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp();

// Docker HEALTHCHECK and VTC both use container-running-and-healthy as the entitlement signal
// for this module (see architecture decision: no separate licensing check).
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

var manifestJson = await File.ReadAllTextAsync(
    Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
app.MapGet("/manifest", () => Results.Text(manifestJson, "application/json"));

app.Run();
