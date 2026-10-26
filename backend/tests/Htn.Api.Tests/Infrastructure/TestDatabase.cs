using Npgsql;
using Testcontainers.PostgreSql;

namespace Htn.Api.Tests.Infrastructure;

/// <summary>
/// A uniquely named database per test fixture. Uses the server in HTN_TEST_DB when set,
/// otherwise starts a postgres:17-alpine container (Testcontainers).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _name;
    private readonly PostgreSqlContainer? _container;

    private TestDatabase(string adminConnectionString, string name, PostgreSqlContainer? container)
    {
        _adminConnectionString = adminConnectionString;
        _name = name;
        _container = container;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var baseCs = Environment.GetEnvironmentVariable("HTN_TEST_DB");
        PostgreSqlContainer? container = null;
        if (string.IsNullOrWhiteSpace(baseCs))
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
            baseCs = container.GetConnectionString();
        }

        var name = "htn_test_" + Guid.NewGuid().ToString("N")[..20];
        await using (var conn = new NpgsqlConnection(baseCs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return new TestDatabase(baseCs, name, container);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        try
        {
            await using var conn = new NpgsqlConnection(_adminConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (_container is not null) await _container.DisposeAsync();
        }
    }
}
