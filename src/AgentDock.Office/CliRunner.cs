using System.Diagnostics;

namespace AgentDock.Office;

/// <summary>
/// Shells out to the AgentDock.Office.Cli binary as a subprocess per the
/// CLI-first architecture decision. The MCP adapter layer calls this, reads
/// the JSON stdout, and adapts it into an MCP tool response.
///
/// Exit codes and stderr are the CLI's error-reporting contract (see
/// DEV_PLAN.md); this class maps them to exceptions that the MCP tool
/// method catches.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Locates the CLI binary next to the MCP server assembly.
    /// </summary>
    public static string ResolveCliPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var os = OperatingSystem.IsWindows() ? ".exe" : "";
        var path = Path.Combine(baseDir, "AgentDock.Office.Cli" + os);

        if (!File.Exists(path))
        {
            // Fallback: maybe it's in a subdirectory
            path = Path.Combine(baseDir, "cli", "AgentDock.Office.Cli" + os);
        }

        return path;
    }

    /// <summary>
    /// Runs the CLI with the given arguments and returns the parsed JSON result.
    /// Throws on non-zero exit code.
    /// </summary>
    public static async Task<string> RunAsync(string arguments, TimeSpan? timeout = null)
    {
        var cliPath = ResolveCliPath();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.Start();

        // Read stdout and stderr concurrently
        var readStdout = process.StandardOutput.ReadToEndAsync();
        var readStderr = process.StandardError.ReadToEndAsync();

        var completed = process.WaitForExit((int)effectiveTimeout.TotalMilliseconds);

        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException(
                $"CLI subprocess timed out after {effectiveTimeout.TotalSeconds}s: {cliPath} {arguments}");
        }

        var stdout = await readStdout;
        var stderr = await readStderr;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CLI subprocess exited with code {process.ExitCode}.\n" +
                $"Command: {cliPath} {arguments}\n" +
                $"Stderr: {stderr.Trim()}");
        }

        return stdout.Trim();
    }
}