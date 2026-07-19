using Npgsql;

namespace CapabilityModule.Office.Database;

/// <summary>
/// Applies pending SQL migrations from the <c>db/migrations/</c> directory
/// embedded in the deployment, and provides a method to verify connectivity.
///
/// Migrations are plain versioned SQL scripts (per Phase 7's decision to
/// default to plain SQL over a .NET migration framework). Each script is
/// applied in order; scripts that have already been applied are tracked in
/// a <c>schema_migrations</c> table.
/// </summary>
internal static class DbInitializer
{
    /// <summary>
    /// Ensures the <c>schema_migrations</c> tracking table exists, then applies
    /// any migration scripts in <c>db/migrations/</c> that have not yet been run.
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public static async Task InitializeAsync(NpgsqlDataSource dataSource)
    {
        await EnsureMigrationTableAsync(dataSource);

        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "db", "migrations");
        if (!Directory.Exists(migrationsDir))
        {
            // No migrations to apply — this is valid for development scenarios
            // where the module is running without the full deployment.
            return;
        }

        var applied = await GetAppliedMigrationsAsync(dataSource);

        var migrationFiles = Directory
            .GetFiles(migrationsDir, "*.sql")
            .Select(f => new
            {
                FilePath = f,
                Name = Path.GetFileNameWithoutExtension(f),
                Script = Path.GetFileName(f)
            })
            .OrderBy(x => x.Name)
            .ToList();

        foreach (var migration in migrationFiles)
        {
            if (applied.Contains(migration.Script))
                continue;

            var sql = await File.ReadAllTextAsync(migration.FilePath);

            await using var conn = await dataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // Apply the migration SQL
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();

                // Record the migration as applied
                await using var recordCmd = conn.CreateCommand();
                recordCmd.CommandText =
                    "INSERT INTO schema_migrations (script_name) VALUES ($1)";
                recordCmd.Parameters.Add(new NpgsqlParameter { Value = migration.Script });
                await recordCmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    /// <summary>
    /// Checks whether the database is reachable by executing a simple query.
    /// Returns <c>true</c> if the connection succeeds and a query can be run;
    /// <c>false</c> otherwise.
    /// </summary>
    public static async Task<bool> CheckHealthAsync(NpgsqlDataSource dataSource)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                script_name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT script_name FROM schema_migrations ORDER BY script_name";
        await using var reader = await cmd.ExecuteReaderAsync();

        var result = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
