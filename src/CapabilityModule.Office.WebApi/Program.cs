using System.Text.Json;
using CapabilityModule.Office.WebApi.Cli;

var builder = WebApplication.CreateBuilder(args);

// Allow larger request bodies for file uploads (matches the CLI's default max upload size)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 60 * 1024 * 1024; // 60 MB (slightly above OFFICE_MAX_UPLOAD_SIZE default)
});

var app = builder.Build();

// Serve the SPA's static files (production build output from wwwroot).
// ASP.NET Core's default static files middleware looks for wwwroot under
// the content root path, so the Docker COPY of web/dist/ to /app/wwwroot
// is picked up automatically.
app.UseDefaultFiles();
app.UseStaticFiles();

// SPA fallback: serve index.html for any non-API route so client-side
// navigation (or a direct page reload) returns the app shell.
app.MapFallbackToFile("index.html");

// A JsonElement fallback for when the CLI's JSON is missing an expected
// "entries" array — independent of any JsonDocument, so it's safe to Clone().
JsonElement EmptyArray;
using (var emptyDoc = JsonDocument.Parse("[]"))
{
    EmptyArray = emptyDoc.RootElement.Clone();
}

// Resolve the CLI root once — used by all endpoints for path security
string ResolveRoot()
{
    var env = Environment.GetEnvironmentVariable("OFFICE_CLI_ROOT");
    if (!string.IsNullOrWhiteSpace(env))
        return env;

    var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
    Directory.CreateDirectory(dataDir);
    return dataDir;
}

// ---------------------------------------------------------------------------
// /health
// ---------------------------------------------------------------------------
app.MapGet("/health", () =>
{
    return Results.Ok(new { status = "healthy" });
});

// ---------------------------------------------------------------------------
// GET /search?q=...&path=...&mode=filename|hybrid
// ---------------------------------------------------------------------------
app.MapGet("/search", async (string q, string? path, string? mode) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });
    }

    var effectiveMode = mode?.ToLowerInvariant() ?? "filename";

    try
    {
        if (effectiveMode == "filename")
        {
            var args = new List<string> { "search", q };
            if (!string.IsNullOrWhiteSpace(path))
            {
                args.Add(path);
            }
            args.Add("--root");
            args.Add(ResolveRoot());

            var json = await CliRunner.RunAsync(args);
            using var doc = JsonDocument.Parse(json);
            var entries = doc.RootElement.TryGetProperty("entries", out var e) ? e.Clone() : EmptyArray;
            return Results.Ok(new
            {
                query = q,
                mode = effectiveMode,
                path = path ?? ".",
                totalResults = entries.ValueKind == JsonValueKind.Array ? entries.GetArrayLength() : 0,
                entries = entries
            });
        }
        else if (effectiveMode == "hybrid")
        {
            var args = new List<string> { "index", "search", q };
            args.Add("--root");
            args.Add(ResolveRoot());
            if (!string.IsNullOrWhiteSpace(path))
            {
                args.Add(path);
            }

            var json = await CliRunner.RunAsync(args, CliRunner.DefaultTimeout);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var entries = root.TryGetProperty("entries", out var e) ? e.Clone() : EmptyArray;
            var totalResults = root.TryGetProperty("totalResults", out var tr) ? tr.GetInt32() : 0;

            return Results.Ok(new
            {
                query = q,
                mode = effectiveMode,
                path = path ?? ".",
                totalResults,
                entries = entries
            });
        }
        else
        {
            return Results.BadRequest(new { error = $"Invalid mode '{mode}'. Use 'filename' or 'hybrid'." });
        }
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ---------------------------------------------------------------------------
// POST /index?path=... — (re)build the search index (wraps `index build`).
// Longer timeout than other endpoints since it walks the directory, extracts
// content, chunks it, and generates embeddings — not a quick lookup.
// ---------------------------------------------------------------------------
var indexTimeout = TimeSpan.FromMinutes(5);

app.MapPost("/index", async (string? path) =>
{
    var root = ResolveRoot();
    var effectivePath = string.IsNullOrWhiteSpace(path) ? "." : path;

    try
    {
        var args = new List<string> { "index", "build", effectivePath, "--root", root };
        var json = await CliRunner.RunAsync(args, indexTimeout);
        using var doc = JsonDocument.Parse(json);
        var root2 = doc.RootElement;

        return Results.Ok(new
        {
            path = effectivePath,
            resolved = root2.TryGetProperty("resolved", out var r) ? r.GetString() : effectivePath,
            filesProcessed = root2.TryGetProperty("filesProcessed", out var fp) ? fp.GetInt32() : 0,
            filesIndexed = root2.TryGetProperty("filesIndexed", out var fi) ? fi.GetInt32() : 0,
            filesUnchanged = root2.TryGetProperty("filesUnchanged", out var fu) ? fu.GetInt32() : 0,
            filesSkipped = root2.TryGetProperty("filesSkipped", out var fs) ? fs.GetInt32() : 0,
            filesWithErrors = root2.TryGetProperty("filesWithErrors", out var fe) ? fe.GetInt32() : 0,
            totalChunksWritten = root2.TryGetProperty("totalChunksWritten", out var tcw) ? tcw.GetInt32() : 0,
            totalChunksEmbedded = root2.TryGetProperty("totalChunksEmbedded", out var tce) ? tce.GetInt32() : 0,
            existingChunksEmbedded = root2.TryGetProperty("existingChunksEmbedded", out var ece) ? ece.GetInt32() : 0,
        });
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ---------------------------------------------------------------------------
// GET /view?path=...
// ---------------------------------------------------------------------------
app.MapGet("/view", async (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Query parameter 'path' is required." });
    }

    var root = ResolveRoot();
    var isDocx = path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    try
    {
        if (isDocx)
        {
            var args = new List<string> { "docx", "read", path, "--root", root };
            var json = await CliRunner.RunAsync(args);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() : "";
            var resolved = doc.RootElement.TryGetProperty("resolved", out var r) ? r.GetString() : path;

            return Results.Ok(new
            {
                path,
                resolved,
                content,
                format = "docx"
            });
        }
        else
        {
            var args = new List<string> { "read", path, "--root", root };
            var json = await CliRunner.RunAsync(args);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() : "";
            var resolved = doc.RootElement.TryGetProperty("resolved", out var r) ? r.GetString() : path;

            return Results.Ok(new
            {
                path,
                resolved,
                content,
                format = "text"
            });
        }
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ---------------------------------------------------------------------------
// POST /upload — multipart/form-data file upload
// ---------------------------------------------------------------------------
app.MapPost("/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Request must be multipart/form-data." });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file provided in the upload." });
    }

    // Read the file path from form data (required)
    var path = form["path"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Form field 'path' is required (relative path within the restricted root)." });
    }

    // Read the mode from form data (optional, defaults to "create")
    var mode = form["mode"].FirstOrDefault() ?? "create";

    try
    {
        // Read file bytes and encode to base64
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var contentBase64 = Convert.ToBase64String(ms.ToArray());

        var args = new List<string> { "upload", path, "--content-base64", contentBase64, "--root", ResolveRoot() };
        if (!string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--mode");
            args.Add(mode);
        }

        var json = await CliRunner.RunAsync(args);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return Results.Ok(new
        {
            path = root.TryGetProperty("path", out var p) ? p.GetString() : path,
            resolved = root.TryGetProperty("resolved", out var r) ? r.GetString() : path,
            bytesWritten = root.TryGetProperty("bytesWritten", out var bw) ? bw.GetInt64() : 0,
            version = root.TryGetProperty("version", out var v) ? v.GetInt32() : (int?)null,
            versionPath = root.TryGetProperty("versionPath", out var vp) ? vp.GetString() : null,
        });
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ---------------------------------------------------------------------------
// POST /edit — find/replace in a .docx document
// ---------------------------------------------------------------------------
app.MapPost("/edit", async (EditRequest body) =>
{
    if (string.IsNullOrWhiteSpace(body.Path))
    {
        return Results.BadRequest(new { error = "'path' is required." });
    }
    if (string.IsNullOrWhiteSpace(body.Find))
    {
        return Results.BadRequest(new { error = "'find' is required." });
    }

    var root = ResolveRoot();

    try
    {
        var args = new List<string>
        {
            "docx", "replace", body.Path, "--find", body.Find, "--replace", body.Replace ?? "", "--root", root
        };

        var json = await CliRunner.RunAsync(args);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;

        return Results.Ok(new
        {
            path = rootEl.TryGetProperty("path", out var p) ? p.GetString() : body.Path,
            resolved = rootEl.TryGetProperty("resolved", out var r) ? r.GetString() : body.Path,
            version = rootEl.TryGetProperty("version", out var v) ? v.GetInt32() : 0,
            versionPath = rootEl.TryGetProperty("versionPath", out var vp) ? vp.GetString() : "",
            lastModifiedUtc = rootEl.TryGetProperty("lastModifiedUtc", out var lm) ? lm.GetString() : "",
        });
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ---------------------------------------------------------------------------
// DELETE /delete?path=...
// ---------------------------------------------------------------------------
app.MapDelete("/delete", async (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { error = "Query parameter 'path' is required." });
    }

    var root = ResolveRoot();

    try
    {
        var args = new List<string> { "delete", path, "--root", root };
        var json = await CliRunner.RunAsync(args);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;

        return Results.Ok(new
        {
            path = rootEl.TryGetProperty("path", out var p) ? p.GetString() : path,
            resolved = rootEl.TryGetProperty("resolved", out var r) ? r.GetString() : path,
            version = rootEl.TryGetProperty("version", out var v) ? v.GetInt32() : 0,
            versionPath = rootEl.TryGetProperty("versionPath", out var vp) ? vp.GetString() : "",
            indexRemoved = rootEl.TryGetProperty("indexRemoved", out var ir) && ir.ValueKind != JsonValueKind.Null
                ? ir.GetBoolean()
                : (bool?)null,
        });
    }
    catch (CliToolException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 502);
    }
    catch (CliTimeoutException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 504);
    }
    catch (Exception ex) when (ex is not ArgumentException)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

// ---------------------------------------------------------------------------
// Request models
// ---------------------------------------------------------------------------
internal record EditRequest(string Path, string Find, string? Replace);