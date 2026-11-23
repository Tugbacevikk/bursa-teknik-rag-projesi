using Microsoft.AspNetCore.HttpOverrides;

namespace BtuAsistan.Api.Configuration;

/// <summary>
/// Behind the web front door (Caddy, PLANNING Faz 6 E4) every request comes from Caddy's
/// address; the student's is in X-Forwarded-For. With ReverseProxy:Enabled the API takes
/// it from there, so the rate limits and logs see the real client. Only from addresses in
/// ReverseProxy:KnownNetworks: anyone else could write any address into the header.
/// </summary>
public static class ReverseProxy
{
    public const string SectionName = "ReverseProxy";

    // The private ranges Docker gives its networks. The API has no port on the host, so
    // inside them the only caller is the front door.
    private static readonly string[] DefaultNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>($"{SectionName}:Enabled");

    public static IServiceCollection AddReverseProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return services;
        }

        var networks = configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : DefaultNetworks;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // One hop: the front door sets the header to the one client address it trusts.
            options.ForwardLimit = 1;

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
