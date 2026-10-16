using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using OpenKogama.Web;

namespace OpenKogama.P2p;

public sealed class TunnelJoin : INatPunchListener
{
    const int DirectWaitMs = 6000;
    const int FindWaitMs = 8000;
    const int ReadyWaitMs = 20_000;

    readonly IPEndPoint[] _rendezvous;
    readonly ConcurrentDictionary<NetPeer, bool> _asked = new();
    readonly string _code;
    readonly bool _relayOnly;
    readonly bool _findOnly;
    readonly NetManager _manager;
    readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ConcurrentDictionary<int, Pipe> _pipes = new();
    readonly ConcurrentDictionary<(Socket Socket, IPEndPoint Game), int> _flows = new();
    readonly ConcurrentDictionary<int, (Socket Socket, IPEndPoint Game)> _games = new();
    readonly object _sync = new();
    NetPeer? _server;
    NetPeer? _dialing;
    Link? _link;
    TcpListener? _tcp;
    Socket? _udp;
    Socket? _udp2012;
    int _port;
    int _lastStream;
    int _lastFlow;
    int _missing;

    TunnelJoin(IPEndPoint[] rendezvous, string code, bool relayOnly, bool findOnly)
    {
        _rendezvous = rendezvous;
        _code = code;
        _relayOnly = relayOnly;
        _findOnly = findOnly;
        var events = new EventBasedNetListener();
        events.ConnectionRequestEvent += request => request.Reject();
        events.PeerConnectedEvent += Connected;
        events.PeerDisconnectedEvent += (peer, _) => Disconnected(peer);
        events.NetworkReceiveEvent += Receive;
        _manager = Tunnel.Manager(events, this);
    }

    public static int Run(string given, string[] options, bool findOnly)
    {
        if (Tunnel.Normalize(given) is not string code)
        {
            Console.WriteLine("error bad code");
            return 2;
        }
        var join = new TunnelJoin(Tunnel.Rendezvous(options), code, options.Contains("--relay"), findOnly);
        Tunnel.ExitWithParent(options, () => join._done.TrySetResult(0));
        return join.Serve();
    }

    int Serve()
    {
        _manager.Start();
        foreach (IPEndPoint endPoint in _rendezvous) _manager.Connect(endPoint, Tunnel.Key);
        _ = Task.Delay(FindWaitMs).ContinueWith(_ =>
        {
            if (_server is null) Finish(_missing > 0 ? "missing" : "error rendezvous not answering", _missing > 0 ? 4 : 3);
        });
        int result = _done.Task.Result;
        _manager.Stop();
        return result;
    }

    void Finish(string line, int result)
    {
        if (_done.Task.IsCompleted) return;
        Console.WriteLine(line);
        _done.TrySetResult(result);
    }

    void Connected(NetPeer peer)
    {
        if (Tunnel.IsAny(peer, _rendezvous))
        {
            _asked[peer] = true;
            Tunnel.Send(peer, Op.Find, _code);
            return;
        }
        if (!Use(new Link(peer))) peer.Disconnect();
    }

    void Disconnected(NetPeer peer)
    {
        Link? link = _link;
        if (link is not null && link.Peer == peer) Finish("closed", 0);
        else if (link is null && peer == _server) Finish("error rendezvous lost", 3);
    }

    void Receive(NetPeer peer, NetPacketReader reader, byte lane, DeliveryMethod method)
    {
        try
        {
            if (reader.AvailableBytes == 0) return;
            if (!Tunnel.IsAny(peer, _rendezvous))
            {
                if (_link is { Relay: 0 } link && link.Peer == peer) Handle(reader.GetRemainingBytesSpan());
                return;
            }
            Op op = (Op)reader.PeekByte();
            if (op == Op.Found)
            {
                reader.GetByte();
                if (Choose(peer)) Found(peer, reader.GetString(1024));
                return;
            }
            if (op == Op.Missing && _server is null)
            {
                if (Interlocked.Increment(ref _missing) == _rendezvous.Length) Finish("missing", 4);
                return;
            }
            if (peer != _server) return;
            switch (op)
            {
                case Op.Missing:
                    Finish("error relay refused", 3);
                    break;
                case Op.Relayed when reader.AvailableBytes >= 5:
                    int id = Tunnel.IdOf(reader.GetRemainingBytesSpan());
                    if (!Use(new Link(peer, id))) peer.Send(Tunnel.Frame(Op.Unrelay, id), 0, DeliveryMethod.ReliableOrdered);
                    break;
                case Op.Wrap when reader.AvailableBytes >= 5:
                    ReadOnlySpan<byte> wrapped = reader.GetRemainingBytesSpan();
                    if (_link is { Relay: > 0 } relay && relay.Relay == Tunnel.IdOf(wrapped)) Handle(wrapped[5..]);
                    break;
                case Op.Unrelay when reader.AvailableBytes >= 5:
                    if (_link is { Relay: > 0 } ended && ended.Relay == Tunnel.IdOf(reader.GetRemainingBytesSpan())) Finish("closed", 0);
                    break;
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"tunnel: {error.Message}");
        }
    }

    bool Choose(NetPeer server)
    {
        lock (_sync)
        {
            if (_server is not null) return false;
            _server = server;
        }
        foreach (NetPeer other in _asked.Keys)
            if (other != server) other.Disconnect();
        return true;
    }

    void Found(NetPeer server, string info)
    {
        if (_findOnly)
        {
            Finish("found " + info, 0);
            return;
        }
        try
        {
            Bind();
        }
        catch (Exception error)
        {
            Finish($"error {error.Message}", 5);
            return;
        }
        Console.WriteLine("found " + info);
        if (_relayOnly)
        {
            Tunnel.Send(server, Op.Relay, _code);
        }
        else
        {
            _manager.NatPunchModule.SendNatIntroduceRequest(new IPEndPoint(server.Address, server.Port), "j:" + _code);
            _ = Task.Delay(DirectWaitMs).ContinueWith(_ =>
            {
                if (_link is null && server.ConnectionState == ConnectionState.Connected) Tunnel.Send(server, Op.Relay, _code);
            });
        }
        _ = Task.Delay(ReadyWaitMs).ContinueWith(_ =>
        {
            if (_link is null) Finish("error host not answering", 3);
        });
    }

    void Bind()
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20000, 60000);
            var tcp = new TcpListener(IPAddress.Loopback, port);
            try
            {
                tcp.Start();
                _udp = Tunnel.Udp(new IPEndPoint(IPAddress.Loopback, port));
                _tcp = tcp;
                _port = port;
                Bind2012();
                return;
            }
            catch (SocketException) when (attempt < 50)
            {
                tcp.Stop();
            }
        }
    }

    void Bind2012()
    {
        try
        {
            _udp2012 = Tunnel.Udp(new IPEndPoint(Tunnel.Address2012, Tunnel.Photon2012));
        }
        catch (SocketException)
        {
            Console.WriteLine("note 127.0.0.3:5055 is busy, so KoGaMa 2012 cannot join");
        }
    }

    bool Use(Link link)
    {
        lock (_sync)
        {
            if (_link is not null) return false;
            _link = link;
        }
        Console.WriteLine($"ready {_port} {(link.Relay == 0 ? "direct" : "relay")}");
        _ = AcceptAsync(link);
        if (_udp is Socket udp) _ = ForwardAsync(link, udp);
        if (_udp2012 is Socket udp2012)
        {
            _ = ForwardAsync(link, udp2012);
            _ = new SocketPolicyServer(843, Tunnel.Address2012).RunAsync();
        }
        return true;
    }

    void Handle(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5) return;
        int id = Tunnel.IdOf(frame);
        switch ((Op)frame[0])
        {
            case Op.Data when _pipes.TryGetValue(id, out Pipe? pipe):
                pipe.Deliver(frame[5..]);
                break;
            case Op.Close when _pipes.TryGetValue(id, out Pipe? pipe):
                pipe.End();
                break;
            case Op.Credit when frame.Length >= 9 && _pipes.TryGetValue(id, out Pipe? pipe):
                pipe.Grant(frame[5..]);
                break;
            case Op.Datagram when _games.TryGetValue(id, out var game):
                game.Socket.SendTo(frame[5..], SocketFlags.None, game.Game);
                break;
        }
    }

    async Task AcceptAsync(Link link)
    {
        while (_tcp is TcpListener tcp)
        {
            Socket socket = await tcp.AcceptSocketAsync();
            socket.NoDelay = true;
            int id = Interlocked.Increment(ref _lastStream);
            var pipe = new Pipe(id, link);
            _pipes[id] = pipe;
            pipe.Open();
            _ = pipe.RunAsync(Task.FromResult(socket), () => _pipes.TryRemove(id, out _));
        }
    }

    async Task ForwardAsync(Link link, Socket udp)
    {
        var buffer = new byte[4096];
        EndPoint anyone = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            SocketReceiveFromResult got = await udp.ReceiveFromAsync(buffer, SocketFlags.None, anyone);
            var game = (IPEndPoint)got.RemoteEndPoint;
            int flow = _flows.GetOrAdd((udp, game), _ =>
            {
                int id = Interlocked.Increment(ref _lastFlow);
                _games[id] = (udp, game);
                return id;
            });
            link.SendDatagram(flow, buffer.AsSpan(0, got.ReceivedBytes));
        }
    }

    public void OnNatIntroductionRequest(IPEndPoint inside, IPEndPoint outside, string token)
    {
    }

    public void OnNatIntroductionSuccess(IPEndPoint target, NatAddressType type, string token)
    {
        lock (_sync)
        {
            if (_link is not null || _dialing is not null) return;
            _dialing = _manager.Connect(target, _code);
        }
    }
}
