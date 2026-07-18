using System.Diagnostics;

namespace AgentDock.Office;

/// <summary>
/// Shells out to the AgentDock.Office.Cli binary as a subprocess per the
/// CLI-first architecture decision. The MCP adapter layer calls this, reads
/// the JSON stdout, and adapts it into an MCP tool response.
///
/// Exit codes and stderr are the CLI's error-reporting contract (see
/// DEV_PLAN.md); this class maps them to typed exceptions that the MCP tool
/// method catches.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Default timeout for CLI subprocess invocations.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Locates the CLI binary next to the MCP server assembly.
    /// Throws <see cref="FileNotFoundException"/> if the binary cannot be found.
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

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"CLI binary not found. Looked at: {Path.Combine(baseDir, "AgentDock.Office.Cli" + os)}");
        }

        return path;
    }

    /// <summary>
    /// Runs the CLI with the given arguments and returns the JSON result.
    /// Throws <see cref="CliToolException"/> on non-zero exit code,
    /// <see cref="CliTimeoutException"/> on timeout, and
    /// <see cref="FileNotFoundException"/> if the CLI binary is missing.
    /// </summary>
    public static async Task<string> RunAsync(string arguments, TimeSpan? timeout = null)
    {
        var cliPath = ResolveCliPath();
        var effectiveTimeout = timeout ?? DefaultTimeout;

        using var cts = new CancellationTokenSource(effectiveTimeout);
        return await RunAsync(cliPath, arguments, effectiveTimeout, cts.Token);
    }

    /// <summary>
    /// Runs the CLI with individually-passed arguments (no manual quoting/escaping
    /// required — each element is passed through as its own argv entry via
    /// <see cref="ProcessStartInfo.ArgumentList"/>). Prefer this overload whenever
    /// any argument value comes from user-controlled content, since building a
    /// single escaped command-line string is error-prone for values containing
    /// backslashes or quotes.
    /// </summary>
    public static async Task<string> RunAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var cliPath = ResolveCliPath();
        var effectiveTimeout = timeout ?? DefaultTimeout;

        using var cts = new CancellationTokenSource(effectiveTimeout);
        return await RunAsync(cliPath, arguments, effectiveTimeout, cts.Token);
    }

    /// <summary>
    /// Runs the CLI with the given arguments, cancellation token, and timeout.
    /// </summary>
    internal static async Task<string> RunAsync(
        string cliPath, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
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

        return await RunProcessAsync(process, cliPath, arguments, timeout, cancellationToken);
    }

    /// <summary>
    /// Runs the CLI with a pre-split argument list, cancellation token, and timeout.
    /// </summary>
    internal static async Task<string> RunAsync(
        string cliPath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        return await RunProcessAsync(process, cliPath, string.Join(' ', arguments), timeout, cancellationToken);
    }

    private static async Task<string> RunProcessAsync(
        Process process, string cliPath, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        process.Start();

        try
        {
            // Read stdout and stderr concurrently
            var readStdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readStderr = process.StandardError.ReadToEndAsync(cancellationToken);

            var completed = process.WaitForExit((int)timeout.TotalMilliseconds);

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
                throw new CliTimeoutException(timeout, $"{cliPath} {arguments}");
            }

            // Await the readers so any exceptions propagate
            var stdout = await readStdout;
            var stderr = await readStderr;

            if (process.ExitCode != 0)
            {
                throw new CliToolException(process.ExitCode, stderr, $"{cliPath} {arguments}");
            }

            return stdout.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation token was triggered (either by timeout or external cancellation)
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            throw new CliTimeoutException(timeout, $"{cliPath} {arguments}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exited"))
        {
            // Process exited during stream read — rare race, surface as a tool error
            throw new CliToolException(-1, "Process exited unexpectedly during output read.", $"{cliPath} {arguments}");
        }
    }
}