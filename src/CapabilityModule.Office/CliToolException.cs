namespace CapabilityModule.Office;

/// <summary>
/// Represents a CLI subprocess that completed (did not time out) but exited
/// with a non-zero exit code. The <see cref="ExitCode"/> and <see cref="Stderr"/>
/// properties carry the CLI's error contract (per DEV_PLAN.md) so the MCP
/// adapter can surface a meaningful error to the calling agent.
/// </summary>
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

/// <summary>
/// Represents a CLI subprocess that did not complete within the configured
/// timeout. The process was killed to prevent the sidecar from hanging.
/// </summary>
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

/// <summary>
/// Represents malformed or unexpected output from a CLI subprocess (e.g.
/// non-JSON stdout when JSON was expected, or a JSON payload that doesn't
/// match the contract).
/// </summary>
internal class CliMalformedOutputException : Exception
{
    public string RawOutput { get; }
    public string CliCommand { get; }

    public CliMalformedOutputException(string message, string rawOutput, string cliCommand, Exception? inner = null)
        : base(message, inner)
    {
        RawOutput = rawOutput;
        CliCommand = cliCommand;
    }
}