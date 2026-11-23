using BtuAsistan.Api.Configuration;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Health;
using BtuAsistan.Api.Modules.Analytics;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Modules.Ingestion;
using BtuAsistan.Api.Modules.Search;
using BtuAsistan.Api.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.Npgsql;

var builder = WebApplication.CreateBuilder(args);

ProductionSecretsGuard.EnsureNoPlaceholders(builder.Configuration, builder.Environment);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
var aiServiceUrl = builder.Configuration["AiService:BaseUrl"]
    ?? throw new InvalidOperationException("AiService:BaseUrl is not configured.");

// Origins of the staff web app when it is served from another origin. Always listed
// explicitly: a wildcard cannot be combined with cookies. Empty means same-origin only.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddOpenApi();

// Errors the framework raises itself (e.g. a body that is not valid JSON) get an RFC 9457
// problem body like the endpoints' own errors, instead of an empty 400.
builder.Services.AddProblemDetails();
// One data source (connection pool) shared by EF Core and the health check.
// UseVector teaches Npgsql the pgvector "vector" type.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(dataSource, npgsql => npgsql.UseVector())
    .UseSnakeCaseNamingConvention());
builder.Services.AddIdentityModule();
builder.Services.AddIngestionModule(aiServiceUrl);
builder.Services.AddSearchModule(builder.Configuration, aiServiceUrl);
builder.Services.AddChatModule(aiServiceUrl);
builder.Services.AddAnalyticsModule();
builder.Services.AddApiRateLimiting();
builder.Services.AddReverseProxyHeaders(builder.Configuration);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddHttpClient<AiServiceHealthCheck>(client =>
{
    client.BaseAddress = new Uri(aiServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<AiServiceHealthCheck>("ai-service");

var app = builder.Build();

if (args is [CreateAdminCommand.Name, ..])
{
    return await CreateAdminCommand.RunAsync(app.Services, args);
}

if (args is [ImportSeedCommand.Name, ..])
{
    return await ImportSeedCommand.RunAsync(app.Services, args);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// First: everything after it (rate limits, logs) must see the client's address, not the
// front door's.
if (ReverseProxy.IsEnabled(app.Configuration))
{
    app.UseForwardedHeaders();
}

app.UseCors();

// Early, so a rejected request costs as little work as possible.
app.UseRateLimiter();

// After CORS, so the browser can read the rejection; before authentication, so a forged
// request is turned away without the token being looked at.
app.UseSessionCookieCsrfProtection();

// Order matters: identify the caller first, then check what they may access.
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();
app.MapAnalyticsEndpoints();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();

// For the container's health check: only "the API is up". /health also reports the AI
// service, which may still be downloading its model for many minutes.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

await app.RunAsync();
return 0;
