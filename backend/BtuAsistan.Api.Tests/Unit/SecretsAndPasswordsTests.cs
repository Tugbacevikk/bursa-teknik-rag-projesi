using BtuAsistan.Api.Configuration;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BtuAsistan.Api.Tests.Unit;

public sealed class TemporaryPasswordTests
{
    // PLANNING D6: read off the screen and typed in by someone else.
    [Fact]
    public void Has_no_characters_easily_confused()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPassword.Create();

            Assert.Equal(TemporaryPassword.Length, password.Length);
            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
            Assert.All(password, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Meets_the_password_rules()
    {
        Assert.True(PasswordRules.IsValid(TemporaryPassword.Create()));
    }

    [Fact]
    public void Is_different_every_time()
    {
        var passwords = Enumerable.Range(0, 100).Select(_ => TemporaryPassword.Create()).ToHashSet();

        Assert.Equal(100, passwords.Count);
    }
}

// Decision 18: outside Development the API refuses the example passwords.
public sealed class ProductionSecretsGuardTests
{
    private const string GoodConnection = "Host=db;Database=btu;Username=app;Password=Xk29fLq0pZ";

    [Theory]
    [InlineData("Host=db;Database=btu;Username=app;Password=change-me-app")]
    [InlineData("Host=db;Database=btu;Username=app;Password=CHANGE-ME")]
    [InlineData("Host=db;Database=btu;Username=app")]
    public void Production_refuses_example_or_missing_database_password(string connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Guard("Production", connection, "a-real-signing-key-of-enough-length-123"));

        Assert.Contains("ConnectionStrings__Default", error.Message);
    }

    [Fact]
    public void Production_refuses_example_signing_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Guard("Production", GoodConnection, "change-me-to-a-long-random-key"));

        Assert.Contains("Jwt__SigningKey", error.Message);
    }

    [Fact]
    public void Production_accepts_real_values()
    {
        Guard("Production", GoodConnection, "a-real-signing-key-of-enough-length-123");
    }

    [Fact]
    public void Development_allows_the_examples()
    {
        Guard("Development", "Host=db;Username=app;Password=change-me-app", "change-me");
    }

    private static void Guard(string environment, string connection, string signingKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connection,
                ["Jwt:SigningKey"] = signingKey,
            })
            .Build();

        ProductionSecretsGuard.EnsureNoPlaceholders(configuration, new Environment(environment));
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "BtuAsistan.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
