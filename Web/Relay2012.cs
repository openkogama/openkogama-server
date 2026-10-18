using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using OpenKogama.P2p;

namespace OpenKogama.Web;

public sealed class Relay2012(int photonPort)
{
    public static readonly IPAddress Address = IPAddress.Parse("127.0.0.4");

    readonly ConcurrentDictionary<IPEndPoint, Socket> _games = new();
    readonly IPEndPoint _server = new(IPAddress.Loopback, photonPort);
    Socket? _listener;

    public bool Start()
    {
        try
        {
            _listener = Tunnel.Udp(new IPEndPoint(Address, Tunnel.Photon2012));
        }
        catch (SocketException)
        {
            return false;
        }
        _ = ReceiveAsync(_listener);
        return true;
    }

    async Task ReceiveAsync(Socket listener)
    {
        var buffer = new byte[4096];
        EndPoint anyone = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            SocketReceiveFromResult got = await listener.ReceiveFromAsync(buffer, SocketFlags.None, anyone);
            Socket relay = _games.GetOrAdd((IPEndPoint)got.RemoteEndPoint, Open);
            await relay.SendToAsync(buffer.AsMemory(0, got.ReceivedBytes), SocketFlags.None, _server);
        }
    }

    Socket Open(IPEndPoint game)
    {
        Socket relay = Tunnel.Udp(new IPEndPoint(IPAddress.Loopback, 0));
        _ = ReturnAsync(relay, game);
        return relay;
    }

    async Task ReturnAsync(Socket relay, IPEndPoint game)
    {
        var buffer = new byte[4096];
        while (true)
        {
            int count = await relay.ReceiveAsync(buffer, SocketFlags.None);
            await _listener!.SendToAsync(buffer.AsMemory(0, count), SocketFlags.None, game);
        }
    }
}
