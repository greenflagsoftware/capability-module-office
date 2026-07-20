using System.CommandLine;
using CapabilityModule.Office.Cli.Commands;
using Npgsql;

namespace CapabilityModule.Office.Cli.Tests;

/// <summary>
/// Verifies that `delete` actually removes the file's indexed content from
/// Postgres, not just from the filesystem — closing the gap where a deleted
/// document kept surfacing as a stale hybrid-search hit because nothing ever
/// cleaned up its `documents`/`chunks` rows. Exercises the real `delete`
/// command (not just <see cref="IndexEngine.RemoveFromIndexAsync"/> directly)
/// against a real Postgres + pgvector container, same as
/// <see cref="IndexEngineTests"/>.
/// </summary>
[Collection("Postgres")]
public sealed class DeleteCommandIndexTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly string _root;
    private readonly RootCommand _rootCmd;

    public DeleteCommandIndexTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _root = Path.Combine(Path.GetTempPath(), "office-cli-delete-index-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
        _rootCmd = new RootCommand { new DeleteCommand().Command() };
    }

    public async Task InitializeAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE documents, chunks RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Delete_IndexedFile_RemovesFileAndIndexEntry()
    {
        var filePath = Path.Combine(_root, "indexed.txt");
        File.WriteAllText(filePath, "Content that gets indexed, then deleted.");
        await IndexEngine.BuildIndexAsync(_root, _root, _postgres.ConnectionString);
        Assert.Equal(1, await CountDocumentsAsync());

        Environment.SetEnvironmentVariable("OFFICE_DB_CONNECTION", _postgres.ConnectionString);
        try
        {
            var exitCode = await _rootCmd.InvokeAsync(new[] { "delete", "indexed.txt", "--root", _root });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFICE_DB_CONNECTION", null);
        }

        Assert.False(File.Exists(filePath), "File should be removed from the filesystem");
        Assert.Equal(0, await CountDocumentsAsync());
    }

    [Fact]
    public async Task Delete_FileNeverIndexed_StillSucceeds_NoIndexRowsAffected()
    {
        var filePath = Path.Combine(_root, "never-indexed.txt");
        File.WriteAllText(filePath, "Never made it into the index.");

        Environment.SetEnvironmentVariable("OFFICE_DB_CONNECTION", _postgres.ConnectionString);
        try
        {
            var exitCode = await _rootCmd.InvokeAsync(new[] { "delete", "never-indexed.txt", "--root", _root });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFICE_DB_CONNECTION", null);
        }

        Assert.False(File.Exists(filePath));
        Assert.Equal(0, await CountDocumentsAsync());
    }

    [Fact]
    public async Task Delete_NoDbConnectionConfigured_StillSucceeds()
    {
        var filePath = Path.Combine(_root, "no-db.txt");
        File.WriteAllText(filePath, "Deleted with no OFFICE_DB_CONNECTION set at all.");

        // Deliberately not setting OFFICE_DB_CONNECTION — delete must not
        // require indexing to be configured.
        var exitCode = await _rootCmd.InvokeAsync(new[] { "delete", "no-db.txt", "--root", _root });

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(filePath));
    }

    private async Task<long> CountDocumentsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
