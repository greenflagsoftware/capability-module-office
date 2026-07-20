namespace CapabilityModule.Office.Cli;

/// <summary>
/// Manages the version store for filesystem operations that mutate existing
/// files. Before a file is overwritten, its current content is snapshotted to
/// <c>_versions/&lt;relative-path&gt;/&lt;original-filename&gt;.v{N}&lt;ext&gt;</c>,
/// providing an undo path without needing a separate "undo" command.
///
/// The version store is kept inside the restricted root so existing
/// <see cref="PathSecurity"/> sandboxing covers it with no new trust boundary.
/// Retention is unbounded — no pruning/expiry of old versions.
/// </summary>
internal static class VersionStore
{
    /// <summary>
    /// Snapshots a file to the version store before it is overwritten.
    /// </summary>
    /// <param name="fullPath">The resolved full path of the file about to be mutated.</param>
    /// <param name="root">The restricted root directory.</param>
    /// <param name="relativePath">The file's path relative to the restricted root.</param>
    /// <returns>A tuple of (versionNumber, versionPath) describing the snapshot.</returns>
    public static (int Version, string VersionPath) Snapshot(
        string fullPath, string root, string relativePath)
    {
        var dir = Path.GetDirectoryName(relativePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var ext = Path.GetExtension(relativePath);

        var versionDir = Path.Combine(root, "_versions", dir);
        Directory.CreateDirectory(versionDir);

        var nextVersion = GetNextVersion(versionDir, fileName, ext);
        var versionFileName = $"{fileName}.v{nextVersion}{ext}";
        var versionPath = Path.Combine(versionDir, versionFileName);

        File.Copy(fullPath, versionPath, overwrite: false);

        return (nextVersion, versionPath);
    }

    /// <summary>
    /// Determines the next version number for a file by scanning the version
    /// store directory for existing <c>.v{N}</c> suffixes.
    /// </summary>
    private static int GetNextVersion(string versionDir, string fileName, string ext)
    {
        if (!Directory.Exists(versionDir))
            return 1;

        var maxVersion = 0;
        var pattern = $"{fileName}.v*{ext}";

        foreach (var existing in Directory.EnumerateFiles(versionDir, pattern))
        {
            var existingName = Path.GetFileNameWithoutExtension(existing);
            // existingName is something like "foo.v1" — extract the number after ".v"
            var dotVIdx = existingName.LastIndexOf(".v", StringComparison.Ordinal);
            if (dotVIdx < 0) continue;

            var numStr = existingName[(dotVIdx + 2)..];
            if (int.TryParse(numStr, out var ver) && ver > maxVersion)
            {
                maxVersion = ver;
            }
        }

        return maxVersion + 1;
    }
}