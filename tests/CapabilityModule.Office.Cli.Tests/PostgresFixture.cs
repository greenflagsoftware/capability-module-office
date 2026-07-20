using Npgsql;
using Testcontainers.PostgreSql;

namespace CapabilityModule.Office.Cli.Tests;

/// <summary>
/// Spins up a real `pgvector/pgvector:pg17` container (the same image
/// `docker-compose.yml` uses) via Testcontainers, and applies the actual
/// migration scripts from `db/migrations/` — the same ones the module runs
/// at startup via `DbInitializer`. One container is shared across every test
/// class in the "Postgres" collection (see <see cref="PostgresCollection"/>)
/// to keep integration-test startup cost to a single container per run;
/// individual test classes are responsible for resetting table state between
/// tests (see the `TRUNCATE` in each test class's `InitializeAsync`).
///
/// Requires Docker to be running locally. There is no CI pipeline in this
/// repo yet (as of Phase 7-11) to gate on Docker availability, so this is a
/// local-dev requirement for now, same as `docker compose up`.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("office_module_test")
            .WithUsername("office_module_test")
            .WithPassword("office_module_test")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ApplyMigrationsAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task ApplyMigrationsAsync()
    {
        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "db", "migrations");
        if (!Directory.Exists(migrationsDir))
        {
            throw new InvalidOperationException(
                $"Migration scripts not found at '{migrationsDir}'. Expected the test project to " +
                "copy db/migrations/*.sql to its output directory — check the <None Include=.../> " +
                "item in CapabilityModule.Office.Cli.Tests.csproj.");
        }

        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);

        foreach (var file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f))
        {
            var sql = await File.ReadAllTextAsync(file);
            await using var conn = await dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>
/// xunit collection definition — every test class decorated with
/// <c>[Collection("Postgres")]</c> shares one <see cref="PostgresFixture"/>
/// instance (one container) instead of paying container-startup cost per class.
/// </summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
