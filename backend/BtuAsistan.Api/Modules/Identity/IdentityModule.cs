using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BtuAsistan.Api.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        // Fails at startup, not at the first login, when the signing key is missing or short.
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JwtTokenIssuer>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names as written in the token ("role", not a long URI).
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    NameClaimType = ClaimNames.Email,
                    RoleClaimType = ClaimNames.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Browsers send the token in the session cookie; other clients (tests, tools)
                // in the Authorization header, which wins when both are present.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (!context.Request.Headers.ContainsKey("Authorization"))
                        {
                            context.Token = context.Request.Cookies[SessionCookie.Name];
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        var staffPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveStaffRequirement(UserRole.Editor, UserRole.Admin))
            .Build();

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, staffPolicy)
            .AddPolicy(Policies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveStaffRequirement(UserRole.Admin)))
            // Secure by default: an endpoint someone forgot to mark is staff-only, not public.
            // Public endpoints opt out with AllowAnonymous().
            .SetFallbackPolicy(staffPolicy);

        services.AddScoped<IAuthorizationHandler, ActiveStaffHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SignOutInactiveAccountsResultHandler>();

        return services;
    }
}
