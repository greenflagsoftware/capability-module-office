var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Streamable HTTP (the default transport) requires stateful sessions.
        // Stateless mode is supported but breaks Inspector and some MCP clients
        // that rely on the Streamable HTTP protocol.
        options.Stateless = false;
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
