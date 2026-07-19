namespace CapabilityModule.Office.Cli.Tests;

public class PathSecurityTests : IDisposable
{
    private readonly string _root;

    public PathSecurityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "office-cli-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_SimpleRelativePath_ResolvesUnderRoot()
    {
        var resolved = PathSecurity.ResolveWithinRoot(_root, "file.txt");

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "file.txt")), resolved);
    }

    [Fact]
    public void ResolveWithinRoot_NestedRelativePath_ResolvesUnderRoot()
    {
        var resolved = PathSecurity.ResolveWithinRoot(_root, Path.Combine("sub", "file.txt"));

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "sub", "file.txt")), resolved);
    }

    [Fact]
    public void ResolveWithinRoot_DotPath_ResolvesToRootItself()
    {
        var resolved = PathSecurity.ResolveWithinRoot(_root, ".");

        Assert.Equal(Path.GetFullPath(_root), resolved);
    }

    [Fact]
    public void ResolveWithinRoot_ParentTraversal_Throws()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSecurity.ResolveWithinRoot(_root, Path.Combine("..", "outside.txt")));
    }

    [Fact]
    public void ResolveWithinRoot_DeepParentTraversal_Throws()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSecurity.ResolveWithinRoot(_root, Path.Combine("sub", "..", "..", "outside.txt")));
    }

    [Fact]
    public void ResolveWithinRoot_LeadingSlash_IsTreatedAsRelativeNotRootOverride()
    {
        // Regression test: Path.Combine(root, "/foo") on Unix returns "/foo",
        // discarding root entirely, unless the leading separator is stripped first.
        var resolved = PathSecurity.ResolveWithinRoot(_root, "/leading-slash.txt");

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "leading-slash.txt")), resolved);
    }

    [Fact]
    public void ResolveWithinRoot_MultipleLeadingSlashes_AreAllStripped()
    {
        var resolved = PathSecurity.ResolveWithinRoot(_root, "///deep.txt");

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "deep.txt")), resolved);
    }

    [Fact]
    public void ResolveWithinRoot_AbsolutePathOutsideRoot_Throws()
    {
        var outside = Path.Combine(Path.GetTempPath(), "office-cli-tests-outside-" + Guid.NewGuid(), "secret.txt");

        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSecurity.ResolveWithinRoot(_root, outside));
    }

    [Fact]
    public void ResolveWithinRoot_SiblingDirectoryWithSamePrefix_Throws()
    {
        // Guards against a naive StartsWith(root) check treating "root-evil" as
        // being inside "root" just because it shares a string prefix.
        var siblingWithSamePrefix = _root + "-evil";
        Directory.CreateDirectory(siblingWithSamePrefix);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathSecurity.ResolveWithinRoot(_root, Path.Combine(siblingWithSamePrefix, "file.txt")));
        }
        finally
        {
            Directory.Delete(siblingWithSamePrefix, recursive: true);
        }
    }

    [Fact]
    public void EffectiveRoot_EmptyOverride_FallsBackToResolveRoot()
    {
        var effective = PathSecurity.EffectiveRoot(string.Empty);

        Assert.Equal(PathSecurity.ResolveRoot(), effective);
    }

    [Fact]
    public void EffectiveRoot_NonEmptyOverride_UsesOverride()
    {
        var effective = PathSecurity.EffectiveRoot(_root);

        Assert.Equal(Path.GetFullPath(_root), effective);
    }
}
