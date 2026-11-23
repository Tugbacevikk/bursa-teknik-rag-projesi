using Npgsql;

namespace BtuAsistan.Api.Configuration;

/// <summary>
/// Outside Development, refuses to start with the placeholder passwords from .env.example
/// and appsettings.Development.json. On the server every secret comes from an environment
/// variable (ConnectionStrings__Default, Jwt__SigningKey); a copied example value means the
/// operator forgot to set one.
/// </summary>
public static class ProductionSecretsGuard
{
    private const string PlaceholderMarker = "change-me";

    public static void EnsureNoPlaceholders(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var problems = new List<string>();

        var connectionString = configuration.GetConnectionString("Default");
        if (connectionString is not null)
        {
            var password = new NpgsqlConnectionStringBuilder(connectionString).Password;
            if (string.IsNullOrEmpty(password) || password.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("ConnectionStrings__Default has no password or uses the example password.");
            }
        }

        var signingKey = configuration["Jwt:SigningKey"];
        if (signingKey is not null && signingKey.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Jwt__SigningKey uses an example value.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start in {environment.EnvironmentName}: {string.Join(" ", problems)}");
        }
    }
}
