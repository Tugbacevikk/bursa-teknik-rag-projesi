using System.Text.RegularExpressions;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BtuAsistan.Eval;

// The evaluation's own database next to the development one (PLANNING S8): created when
// missing, brought to the latest migration, never touching the real documents.
public static partial class EvalDatabase
{
    public const string EvalUserEmail = "eval@btu.test";

    public static async Task PrepareAsync(string connectionString, bool reset, CancellationToken cancellationToken)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var name = target.Database ?? throw new InvalidOperationException("The connection string names no database.");

        // The name goes into CREATE/DROP DATABASE, which cannot take parameters.
        if (!SafeName().IsMatch(name))
        {
            throw new InvalidOperationException($"Unexpected database name '{name}'.");
        }

        var server = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);

            if (reset)
            {
                await Execute(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", cancellationToken);
            }

            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", name);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
            {
                await Execute(connection, $"CREATE DATABASE \"{name}\"", cancellationToken);
                Console.WriteLine($"Created database {name}.");
            }
        }
    }

    // Migrations and the uploader account the documents need. The account has no usable
    // password ("!" is not a hash), so nobody can sign in with it.
    public static async Task<Guid> MigrateAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);

        var normalized = User.NormalizeEmail(EvalUserEmail);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                Email = EvalUserEmail,
                NormalizedEmail = normalized,
                PasswordHash = "!",
                Role = UserRole.Editor,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        return user.Id;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeName();
}
