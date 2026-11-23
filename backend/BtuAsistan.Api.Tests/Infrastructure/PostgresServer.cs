using System.Security.Cryptography;
using BtuAsistan.Api.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BtuAsistan.Api.Tests.Infrastructure;

/// <summary>
/// One real PostgreSQL + pgvector for the whole test run (PLANNING Testler, T2), set up as
/// in production: the init scripts in docker/postgres/init create the extensions and the
/// restricted account, the migrations run as the table owner. That database is then only a
/// template: every test class gets a copy of its own (CREATE DATABASE … TEMPLATE), which
/// takes milliseconds, and connects to it with the restricted account like the API does.
/// </summary>
public sealed class PostgresServer
{
    public const string OwnerUser = "btu_asistan";
    public const string AppUser = "btu_asistan_app";
    private const string TemplateDatabase = "btu_asistan";

    private static readonly Lazy<Task<PostgresServer>> Shared = new(StartAsync);

    private readonly PostgreSqlContainer container;
    private readonly string appPassword;

    // Copies are made one at a time: CREATE DATABASE … TEMPLATE fails while another
    // session is using the template.
    private readonly SemaphoreSlim createLock = new(1, 1);

    private PostgresServer(PostgreSqlContainer container, string appPassword)
    {
        this.container = container;
        this.appPassword = appPassword;
    }

    public static Task<PostgresServer> GetAsync() => Shared.Value;

    /// <summary>
    /// A fresh database with every migration applied; returns the connection string of the
    /// restricted account (what the API gets) and of the owner (for test setup only).
    /// </summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N")[..12];

        await createLock.WaitAsync();
        try
        {
            // Through the maintenance database: no session may be open on the template. One
            // closed a moment ago may still be ending on the server, so any left are ended.
            await using var connection = new NpgsqlConnection(OwnerConnectionString("postgres"));
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TemplateDatabase}' AND pid <> pg_backend_pid()", connection))
            {
                await terminate.ExecuteNonQueryAsync();
            }

            await using var command = new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase} OWNER {OwnerUser}", connection);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            createLock.Release();
        }

        return new TestDatabase(AppConnectionString(name), OwnerConnectionString(name));
    }

    private static async Task<PostgresServer> StartAsync()
    {
        var appPassword = RandomPassword();
        var container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithUsername(OwnerUser)
            .WithPassword(RandomPassword())
            .WithDatabase(TemplateDatabase)
            .WithEnvironment("APP_DB_PASSWORD", appPassword)
            .WithResourceMapping(DirectoryPath.Of(FindInitScripts()), DirectoryPath.Of("/docker-entrypoint-initdb.d/"))
            .Build();

        await container.StartAsync();
        var server = new PostgresServer(container, appPassword);

        // As `migrate` does in production: the owner account, the same migrations.
        var templateConnection = new NpgsqlConnectionStringBuilder(server.OwnerConnectionString(TemplateDatabase)) { Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(templateConnection, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // No pooled connection may stay open on the template.
        NpgsqlConnection.ClearAllPools();
        return server;
    }

    private string OwnerConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;

    private string AppConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = database,
            Username = AppUser,
            Password = appPassword,
        }.ConnectionString;

    // The repository's own init scripts, found from the test binaries upwards.
    private static string FindInitScripts()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "docker", "postgres", "init");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("docker/postgres/init was not found above the test binaries.");
    }

    // Never "change-me…": the API's production guard would refuse it.
    private static string RandomPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

public sealed record TestDatabase(string AppConnectionString, string OwnerConnectionString);
