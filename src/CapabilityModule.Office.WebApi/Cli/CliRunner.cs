using System.Diagnostics;

namespace CapabilityModule.Office.WebApi.Cli;

/// <summary>
/// Shells out to the CapabilityModule.Office.Cli binary as a subprocess per the
/// CLI-first architecture decision. The WebApi layer calls this, reads the JSON
/// stdout, and adapts it into an HTTP response.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Default timeout for CLI subprocess invocations.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Locates the CLI binary next to the WebApi server assembly.
    /// </summary>
    public static string ResolveCliPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var os = OperatingSystem.IsWindows() ? ".exe" : "";
        var path = Path.Combine(baseDir, "CapabilityModule.Office.Cli" + os);

        if (!File.Exists(path))
        {
            path = Path.Combine(baseDir, "cli", "CapabilityModule.Office.Cli" + os);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"CLI binary not found. Looked at: {Path.Combine(baseDir, "CapabilityModule.Office.Cli" + os)}");
        }

        return path;
    }

    /// <summary>
    /// Runs the CLI with individually-passed arguments (no manual quoting/escaping
    /// required). Returns the trimmed stdout on success, or throws on failure.
    /// </summary>
    public static async Task<string> RunAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var cliPath = ResolveCliPath();
        var effectiveTimeout = timeout ?? DefaultTimeout;

        using var cts = new CancellationTokenSource(effectiveTimeout);
        return await RunAsync(cliPath, arguments, effectiveTimeout, cts.Token);
    }

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

        process.Start();

        try
        {
            var readStdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readStderr = process.StandardError.ReadToEndAsync(cancellationToken);

            var completed = process.WaitForExit((int)timeout.TotalMilliseconds);

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
                throw new CliTimeoutException(timeout, $"{cliPath} {string.Join(' ', arguments)}");
            }

            var stdout = await readStdout;
            var stderr = await readStderr;

            if (process.ExitCode != 0)
            {
                throw new CliToolException(process.ExitCode, stderr, $"{cliPath} {string.Join(' ', arguments)}");
            }

            return stdout.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new CliTimeoutException(timeout, $"{cliPath} {string.Join(' ', arguments)}");
        }
    }
}

internal class CliToolException : Exception
{
    public int ExitCode { get; }
    public string Stderr { get; }
    public string CliCommand { get; }

    public CliToolException(int exitCode, string stderr, string cliCommand)
        : base($"CLI tool exited with code {exitCode}. Stderr: {stderr.Trim()}")
    {
        ExitCode = exitCode;
        Stderr = stderr;
        CliCommand = cliCommand;
    }
}

internal class CliTimeoutException : TimeoutException
{
    public TimeSpan Timeout { get; }
    public string CliCommand { get; }

    public CliTimeoutException(TimeSpan timeout, string cliCommand)
        : base($"CLI tool timed out after {timeout.TotalSeconds}s and was killed.")
    {
        Timeout = timeout;
        CliCommand = cliCommand;
    }
}