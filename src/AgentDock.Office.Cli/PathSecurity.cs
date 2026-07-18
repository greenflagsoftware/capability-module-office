namespace AgentDock.Office.Cli;

/// <summary>
/// Resolves a user-supplied path relative to the restricted root and rejects it
/// if it would escape (or is outside) the root. This lives in its own class so
/// read, write, and list share the same sandboxing logic.
/// </summary>
internal static class PathSecurity
{
    /// <summary>
    /// Returns the effective root directory. Looks for the <c>OFFICE_CLI_ROOT</c>
    /// environment variable first; falls back to the current working directory.
    /// </summary>
    public static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("OFFICE_CLI_ROOT");
        return !string.IsNullOrWhiteSpace(env)
            ? Path.GetFullPath(env)
            : Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Resolves the effective root from a command-line <c>--root</c> override or
    /// the default. Simplifies the pattern used in every command handler.
    /// </summary>
    public static string EffectiveRoot(string rootOverride)
    {
        return !string.IsNullOrWhiteSpace(rootOverride)
            ? Path.GetFullPath(rootOverride)
            : ResolveRoot();
    }

    /// <summary>
    /// Resolves <paramref name="userPath"/> (which may be relative or absolute)
    /// against <paramref name="root"/> and returns the canonical full path — but
    /// only if it lies within <paramref name="root"/>. Throws <see cref="UnauthorizedAccessException"/>
    /// if the resolved path escapes the root.
    ///
    /// Leading directory separators on userPath are stripped so that
    /// <c>Path.Combine(root, "/foo")</c> does not discard the root on Unix.
    /// </summary>
    public static string ResolveWithinRoot(string root, string userPath)
    {
        var rootFull = Path.GetFullPath(root);

        // Strip leading separators from userPath so it's treated as relative.
        // Path.Combine(root, "/foo") returns "/foo" on Unix, discarding the root.
        var safePath = userPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(rootFull, safePath));

        // The resolved path is allowed only if it equals the root exactly
        // or starts with root + directory separator (i.e. is a child).
        var rootWithSep = rootFull + Path.DirectorySeparatorChar;

        if (!string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase)
            && !combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{userPath}' resolves outside the restricted root '{root}'.");
        }

        return combined;
    }
}