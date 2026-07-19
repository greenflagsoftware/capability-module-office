using System.CommandLine;

namespace CapabilityModule.Office.Cli.Commands;

/// <summary>
/// Shared options used across multiple commands — keeps the option definitions
/// consistent and avoids duplication.
/// </summary>
internal static class SharedOptions
{
    public static Option<string> RootOption() =>
        new("--root", () => string.Empty,
            "Override the restricted root directory. Defaults to $OFFICE_CLI_ROOT or the current directory.");
}