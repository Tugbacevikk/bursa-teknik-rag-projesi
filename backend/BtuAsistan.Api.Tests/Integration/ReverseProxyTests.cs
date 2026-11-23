using System.Net;
using System.Net.Http.Json;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BtuAsistan.Api.Tests.Integration;

// Faz 6 E4: behind the front door the client's address comes from X-Forwarded-For, but only
// when the request comes from a trusted network (Docker's private ranges); anyone else's
// header is ignored, or a client could pick a new address for every request.
public sealed class ReverseProxyTests(ReverseProxyTests.Fixture api) : IClassFixture<ReverseProxyTests.Fixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // One sign-in attempt from a peer address, claiming a client address.
    private async Task<HttpStatusCode> SignInAsync(string peer, string claimedClient)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "x@btu.test", password = "wrong-password-123" }),
        };
        request.Headers.Add(Fixture.PeerHeader, peer);
        request.Headers.Add("X-Forwarded-For", claimedClient);
        return (await api.Anonymous().SendAsync(request, Cancel)).StatusCode;
    }

    [Fact]
    public async Task Through_the_front_door_each_client_is_counted_on_its_own()
    {
        const string frontDoor = "172.18.0.5";

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(frontDoor, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(frontDoor, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInAsync(frontDoor, "198.51.100.1"));

        // Another student behind the same front door has attempts of their own.
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(frontDoor, "198.51.100.2"));
    }

    [Fact]
    public async Task Header_from_an_untrusted_address_is_ignored()
    {
        const string outsider = "203.0.113.9";

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(outsider, "192.0.2.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(outsider, "192.0.2.2"));

        // A new claimed address each time does not get new attempts.
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInAsync(outsider, "192.0.2.3"));
    }

    public sealed class Fixture : ApiFixture
    {
        // The in-memory server has no network, so a test says which address the request
        // came from through this header (read before the API's own pipeline).
        public const string PeerHeader = "X-Test-Peer-Address";

        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = "true",
            ["RateLimiting:LoginPermitLimit"] = "2",
        };

        protected override void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton<IStartupFilter, PeerAddressFilter>();

        private sealed class PeerAddressFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(PeerHeader, out var peer))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                    }

                    return nextMiddleware(context);
                });
                next(app);
            };
        }
    }
}
