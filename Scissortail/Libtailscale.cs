using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Scissortail;

public record ServerId(int Id);

public class TailscaleConnection : IDisposable {
    internal int Id { get; set; }
    private TailscaleListener Listener { get; set; }
    public Stream Stream { get; init; }

    public string Name => $"Tailscale-Connection-{Id}";

    internal TailscaleConnection(int connectionId, int socketFd, TailscaleListener listener) {
        Id = connectionId;
        Listener = listener;
        Stream = new NamedPipeServerStream(PipeDirection.InOut, true, true, new SafePipeHandle(socketFd, true));
    }

    public unsafe string? GetRemoteAddr() {
        var buf = stackalloc byte[512];
        var res = Libtailscale.GetRemoteAddr(Listener.Id, Id, buf, 512);
        return res switch {
            0 => Utf8StringMarshaller.ConvertToManaged(buf),
            Libtailscale.EBADF => throw new Exception("Invalid Tailscale server, listener, or connection"),
            Libtailscale.ERANGE => throw new Exception("Insufficient buffer size for call to tailscale_getremoteaddr"),
            _ => throw new Exception("unhandled return value from tailscale_getremoteaddr"),
        };
    }

    public async Task<WhoIsResult?> WhoIs() {
        string addr = GetRemoteAddr()!;
        return await Listener.Tailscale.WhoIs(addr);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stream.Dispose();
    }
}

public class TailscaleListener {
    internal int Id { get; set; }
    internal Tailscale Tailscale { get; set; }

    internal TailscaleListener(int id, Tailscale tailscale) {
        Id = id;
        Tailscale = tailscale;
    }

    public unsafe TailscaleConnection? Accept() {
        Socket s = new(new SafeSocketHandle(Id, false));
        List<Socket> sockets = [s];
        Socket.Select(sockets, null, null, 100_000);
        if (sockets.Count == 0) return null;
        var res = Libtailscale.Accept(Id, out int connectionId, out int socketFd);
        return res switch
        {
            Libtailscale.EBADF => throw new Exception("Invalid listener"),
            -1 => throw new Exception(Libtailscale.GetError(Tailscale.ServerId.Id)),
            0 => new TailscaleConnection(connectionId, socketFd, this),
            _ => throw new Exception("Unhandled tailscale_accept return value"),
        };
    }
}

public class Tailscale {
    internal ServerId ServerId { get; set; }
    private string? ProxyCred { get; set; }
    private string? LocalApiCred { get; set; }
    private bool LoopbackStarted { get; set; } = false;
    // Should I take in a IHttpClientFactory instead?
    private HttpClient? LocalApiClient { get; set; }

    public Tailscale() {
        ServerId = new(Libtailscale.New());
    }

    public void Start() {
        var res = Libtailscale.Start(ServerId.Id);
        if (res != 0) {
            throw new Exception(Libtailscale.GetError(ServerId.Id));
        }
    }

    public void Close() {
        var res = Libtailscale.Close(ServerId.Id);
        if (res != 0) {
            throw new Exception(Libtailscale.GetError(ServerId.Id));
        }
    }

    public unsafe void StartLoopbackServer() {
        var addrSize = 1024;
        var addrBuf = stackalloc byte[addrSize];
        var proxyCredBuf = stackalloc byte[33];
        var localApiCredBuf = stackalloc byte[33];
        var res = Libtailscale.Loopback(ServerId.Id, addrBuf, addrSize, proxyCredBuf, localApiCredBuf);
        if (res != 0) {
            throw new Exception(Libtailscale.GetError(ServerId.Id));
        }
        var localApiAddr = Utf8StringMarshaller.ConvertToManaged(addrBuf);
        ProxyCred = Utf8StringMarshaller.ConvertToManaged(proxyCredBuf);
        LocalApiCred = Utf8StringMarshaller.ConvertToManaged(localApiCredBuf);
        LoopbackStarted = true;
        LocalApiClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{localApiAddr}")
        };
        LocalApiClient.DefaultRequestHeaders.Add("Sec-Tailscale", "localapi");
        LocalApiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{LocalApiCred}"!)));
    }

    internal async Task<WhoIsResult?> WhoIs(string addr) {
        RequireLoopback();
        var resp = await LocalApiClient!.GetAsync($"/localapi/v0/whois?addr={Uri.EscapeDataString(addr)}");
        return await resp.Content.ReadFromJsonAsync<WhoIsResult>();
    }

    private void RequireLoopback() {
        if (!LoopbackStarted) throw new Exception("You must start the Tailscale loopback server with StartLoopbackServer() before performing this operation.");
    }

    public unsafe TailscaleListener Listen() {
        var res = Libtailscale.Listen(ServerId.Id, "tcp", ":8080", out int listenerId);
        if (res != 0) {
            throw new Exception(Libtailscale.GetError(ServerId.Id));
        }
        return new TailscaleListener(listenerId, this);
    }
}

public unsafe static partial class Libtailscale {
    internal const int EBADF = 0x9;
    internal const int ERANGE = 0x22;

    [LibraryImport("libtailscale", EntryPoint = "tailscale_new")]
    internal static partial int New();
    [LibraryImport("libtailscale", EntryPoint = "tailscale_start")]
    internal static partial int Start(int sd);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_up")]
    internal static partial int Up();
    [LibraryImport("libtailscale", EntryPoint = "tailscale_close")]
    internal static partial int Close();
    [LibraryImport("libtailscale", EntryPoint = "tailscale_errmsg")]
    internal static partial int ErrMsg(int sd, byte* buf, int buflen);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_listen", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Listen(int sd, string network, string addr, out int listenerId);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_accept")]
    internal static partial int Accept(int listenerId, out int connectionId, out int socketFd);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_close")]
    internal static partial int Close(int serverId);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_loopback")]
    internal static partial int Loopback(int serverId, byte* addrOut, int addrLen, byte* proxyCredOut, byte* localApiCredOut);
    [LibraryImport("libtailscale", EntryPoint = "tailscale_getremoteaddr")]
    internal static partial int GetRemoteAddr(int listenerId, int connectionId, byte* bufOut, int bufLen);

    internal static string? GetError(int sd) {
        var size = 512;
        var buf = stackalloc byte[size];
        var res = ErrMsg(sd, buf, size);
        return res switch
        {
            0 => Utf8StringMarshaller.ConvertToManaged(buf),
            EBADF => throw new Exception("invalid sd"),
            ERANGE => throw new Exception("insufficient buffer size for err msg"),
            _ => throw new Exception("unhandled result from tailscale_errmsg"),
        };
    }
}
