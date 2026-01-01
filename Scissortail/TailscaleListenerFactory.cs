using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Nerdbank.Streams;

namespace Scissortail;

public static class TailscaleDefaults {
    public const string AuthenticationType = "TailscaleAuthentication";
}

public class TailscaleEndpoint : EndPoint {
    public override AddressFamily AddressFamily => AddressFamily.Unspecified;
}

public class TailscaleConnectionContext(FeatureCollection features) : ConnectionContext
{
    public required override IDuplexPipe Transport { get; set; }
    public required override string ConnectionId { get; set; }
    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
    private readonly FeatureCollection _Features = features;
    public override IFeatureCollection Features { get => _Features; }
}

public class TailscaleHttpAuthenticationFeature : IHttpAuthenticationFeature
{
    public TailscaleHttpAuthenticationFeature(WhoIsResult whoIs) {
        List<Claim> claims = [
            new(ClaimTypes.Name, whoIs.UserProfile.DisplayName),
            // I'm not sure if LoginName is always an email, but it's probably a safe bet
            new(ClaimTypes.Email, whoIs.UserProfile.LoginName),
            new("picture", whoIs.UserProfile.ProfilePicURL),
        ];
        var ident = new ClaimsIdentity(claims, TailscaleDefaults.AuthenticationType);
        User = new ClaimsPrincipal(ident);
    }

    public ClaimsPrincipal? User { get; set; }
}

public class TailscaleConnectionListener(EndPoint _EndPoint, TailscaleListener Listener) : IConnectionListener
{
    public EndPoint EndPoint => _EndPoint;
    bool Bound = true;

    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        while (Bound && !cancellationToken.IsCancellationRequested) {
            var conn = Listener.Accept();
            if (conn is null) {
                continue;
            }

            if (!conn.Stream.CanRead || !conn.Stream.CanWrite) {
                return null;
            }
            var pipe = conn.Stream.UsePipe(sizeHint: 8192, cancellationToken: cancellationToken);
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature());
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            var whoIs = await conn.WhoIs() ?? throw new Exception();
            features.Set<IHttpAuthenticationFeature>(new TailscaleHttpAuthenticationFeature(whoIs));
            return (ConnectionContext?)new TailscaleConnectionContext(features) {
                Transport = pipe,
                ConnectionId = conn.Name,
            };
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Bound = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        Bound = false;
        return ValueTask.CompletedTask;
    }
}

public class TailscaleListenerFactory : IConnectionListenerFactory, IConnectionListenerFactorySelector, IDisposable
{
    private Tailscale Server { get; set; } = new();

    public async ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        Server.Start();
        var listener = Server.Listen();
        Server.StartLoopbackServer();
        return new TailscaleConnectionListener(endpoint, listener);
    }

    public bool CanBind(EndPoint endpoint)
    {
        return endpoint is TailscaleEndpoint;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Server?.Close();
    }
}

public static class WebHostBuilderExtensions {
    public static IWebHostBuilder UseTailscale(this IWebHostBuilder host) {
        if (host is ConfigureWebHostBuilder) {
            host.ConfigureKestrel(opts => {
                opts.Listen(new TailscaleEndpoint());
            });
        }
        return host.ConfigureServices(services => {
            services.AddSingleton<IConnectionListenerFactory, TailscaleListenerFactory>();
        });
    }
}
