using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Embeddings;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BtuAsistan.Api.Tests.Infrastructure;

/// <summary>
/// The API running in memory (no network port) against a database of its own, with the AI
/// service replaced by fakes (PLANNING Testler, T2–T3). One per test class
/// (IClassFixture&lt;ApiFixture&gt;); a class that needs other settings derives from it and
/// overrides <see cref="Settings"/>.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    private ApiFactory? factory;

    public TestDatabase Database { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory => factory ?? throw new InvalidOperationException("Not initialized.");

    public FakeEmbeddingClient Embeddings => Factory.Services.GetRequiredService<FakeEmbeddingClient>();

    public FakeAnswerGenerator Answers => Factory.Services.GetRequiredService<FakeAnswerGenerator>();

    /// <summary>
    /// Configuration on top of the defaults below, as "Section:Key" pairs. The defaults
    /// raise every rate limit, so tests do not trip over each other's requests; the tests
    /// of the limits lower them.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>
    /// Rows written before the API starts, as the table owner: for what the background
    /// services do at startup (e.g. the retention clean-up).
    /// </summary>
    protected virtual Task SeedBeforeStartAsync(Npgsql.NpgsqlConnection owner) => Task.CompletedTask;

    /// <summary>Extra test services, after the fakes are in place.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public virtual async ValueTask InitializeAsync()
    {
        var server = await PostgresServer.GetAsync();
        Database = await server.CreateDatabaseAsync();

        await using (var owner = new Npgsql.NpgsqlConnection(Database.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await SeedBeforeStartAsync(owner);
        }

        factory = new ApiFactory(Database, Settings, ConfigureServices);

        // Starts the host now (and its background services), not at the first request.
        _ = factory.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    // HTTPS, so the session cookie (Secure outside Development) is kept and sent back.
    public HttpClient Anonymous() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>A staff account written straight to the database, with a random password.</summary>
    public async Task<TestAccount> CreateStaffAsync(UserRole role = UserRole.Admin, bool mustChangePassword = false, bool isActive = true)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@btu.test";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            NormalizedEmail = User.NormalizeEmail(email),
            PasswordHash = string.Empty,
            Role = role,
            IsActive = isActive,
            MustChangePassword = mustChangePassword,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new TestAccount(user.Id, email, password, role);
    }

    /// <summary>Signs in through the API and returns a client sending the token.</summary>
    public async Task<HttpClient> SignInAsync(TestAccount account)
    {
        var client = Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account.Email, account.Password }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    public async Task<HttpClient> SignInAsNewAsync(UserRole role = UserRole.Admin) =>
        await SignInAsync(await CreateStaffAsync(role));

    /// <summary>Runs code against the database with the API's own (restricted) account.</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action) =>
        await WithDbAsync(async db =>
        {
            await action(db);
            return 0;
        });

    private sealed class ApiFactory(
        TestDatabase database, IReadOnlyDictionary<string, string?> overrides, Action<IServiceCollection> configureServices)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not Development: no user-secrets or appsettings.Development.json, and the
            // production guards are active, as on the server.
            builder.UseEnvironment("Testing");

            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = database.AppConnectionString,
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                // Never called: every client of it is replaced below.
                ["AiService:BaseUrl"] = "http://ai-service.invalid",
                ["RateLimiting:LoginPermitLimit"] = "1000",
                ["RateLimiting:ChatPerMinute"] = "1000",
                ["RateLimiting:ChatPerHour"] = "10000",
                ["RateLimiting:FeedbackPerMinute"] = "1000",
                // Only what goes wrong in the test output, not every query.
                ["Logging:LogLevel:Default"] = "Warning",
            };
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }

            // UseSetting, not ConfigureAppConfiguration: Program reads some of these before
            // the host is built.
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmbeddingClient>();
                services.AddSingleton<FakeEmbeddingClient>();
                services.AddSingleton<IEmbeddingClient>(provider => provider.GetRequiredService<FakeEmbeddingClient>());

                services.RemoveAll<IAnswerGenerator>();
                services.AddSingleton<FakeAnswerGenerator>();
                services.AddSingleton<IAnswerGenerator>(provider => provider.GetRequiredService<FakeAnswerGenerator>());

                configureServices(services);
            });
        }
    }
}

public sealed record TestAccount(Guid Id, string Email, string Password, UserRole Role);
