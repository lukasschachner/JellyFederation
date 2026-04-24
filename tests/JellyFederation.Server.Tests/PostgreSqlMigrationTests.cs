using DotNet.Testcontainers.Builders;
using JellyFederation.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class PostgreSqlMigrationTests
{
    private static readonly ExpectedIndex[] ExpectedPerformanceIndexes =
    [
        new("Invitations", "IX_Invitations_FromServerId_Status", ["FromServerId", "Status"]),
        new("Invitations", "IX_Invitations_ToServerId_Status", ["ToServerId", "Status"]),
        new("FileRequests", "IX_FileRequests_OwningServerId_Status", ["OwningServerId", "Status"]),
        new("FileRequests", "IX_FileRequests_RequestingServerId_Status", ["RequestingServerId", "Status"]),
        new("FileRequests", "IX_FileRequests_Status_CreatedAt", ["Status", "CreatedAt"])
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSql_MigrationsApply_AndPerformanceIndexesExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("jellyfederation_tests")
            .WithUsername("jellyfederation")
            .WithPassword("jellyfederation")
            .Build();

        await StartOrSkipIfDockerUnavailableAsync(postgres, cancellationToken);

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<FederationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("JellyFederation.Migrations.PostgreSQL"))
            .Options;

        await using (var db = new FederationDbContext(options))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await AssertMigrationAppliedAsync(connection, "20260423184333_AddPerformanceIndexes", cancellationToken);

        foreach (var expectedIndex in ExpectedPerformanceIndexes)
        {
            var actualColumns = await GetIndexColumnsAsync(connection, expectedIndex, cancellationToken);
            Assert.NotNull(actualColumns);
            Assert.Equal(expectedIndex.Columns, actualColumns);
        }
    }

    private static async Task StartOrSkipIfDockerUnavailableAsync(
        PostgreSqlContainer postgres,
        CancellationToken cancellationToken)
    {
        try
        {
            await postgres.StartAsync(cancellationToken);
        }
        catch (DockerUnavailableException ex)
        {
            Assert.Skip($"Docker is unavailable: {ex.Message}");
        }
        catch (DockerConfigurationException ex)
        {
            Assert.Skip($"Docker is not configured for Testcontainers: {ex.Message}");
        }
    }

    private static async Task AssertMigrationAppliedAsync(
        NpgsqlConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = @migrationId;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migrationId", migrationId);

        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        Assert.Equal(1L, count);
    }

    private static async Task<string[]?> GetIndexColumnsAsync(
        NpgsqlConnection connection,
        ExpectedIndex expectedIndex,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT array_agg(a.attname ORDER BY keys.ordinality)::text[]
            FROM pg_class table_class
            JOIN pg_namespace table_namespace ON table_namespace.oid = table_class.relnamespace
            JOIN pg_index index_definition ON index_definition.indrelid = table_class.oid
            JOIN pg_class index_class ON index_class.oid = index_definition.indexrelid
            JOIN LATERAL unnest(index_definition.indkey) WITH ORDINALITY AS keys(attnum, ordinality) ON true
            JOIN pg_attribute a ON a.attrelid = table_class.oid AND a.attnum = keys.attnum
            WHERE table_namespace.nspname = 'public'
              AND table_class.relname = @tableName
              AND index_class.relname = @indexName
            GROUP BY index_class.relname;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tableName", expectedIndex.TableName);
        command.Parameters.AddWithValue("indexName", expectedIndex.IndexName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return reader.GetFieldValue<string[]>(0);
    }

    private sealed record ExpectedIndex(string TableName, string IndexName, string[] Columns);
}
