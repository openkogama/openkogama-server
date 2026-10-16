using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using LiteNetLib;
using OpenKogama.PublicList;

namespace OpenKogama.P2p;

public sealed class TunnelHost : INatPunchListener
{
    const int MaxGuests = 16;
    const int TickMs = 5000;
    const int PunchEvery = 3;

    readonly IPEndPoint[] _rendezvous;
    readonly Dictionary<IPEndPoint, NetPeer> _dialing = [];
    readonly object _sync = new();
    readonly string _secret;
    readonly string _info;
    readonly int _http;
    readonly int _photon;
    readonly NetManager _manager;
    readonly ConcurrentDictionary<NetPeer, Guest> _direct = new();
    readonly ConcurrentDictionary<int, Guest> _relayed = new();
    readonly ManualResetEventSlim _stopped = new();
    NetPeer? _server;
    string? _code;
    int _guests = -1;

    TunnelHost(IPEndPoint[] rendezvous, string secret, string info, int http, int photon)
    {
        _rendezvous = rendezvous;
        _secret = secret;
        _info = info;
        _http = http;
        _photon = photon;
        var events = new EventBasedNetListener();
        events.ConnectionRequestEvent += Request;
        events.PeerConnectedEvent += Connected;
        events.PeerDisconnectedEvent += (peer, _) => Disconnected(peer);
        events.NetworkReceiveEvent += Receive;
        _manager = Tunnel.Manager(events, this);
    }

    public static int Run(string[] options)
    {
        string secret = Tunnel.Option(options, "secret") ?? Tunnel.RandomCode() + Tunnel.RandomCode() + Tunnel.RandomCode();
        string info = Tunnel.Option(options, "info") ?? "{}";
        int http = int.TryParse(Tunnel.Option(options, "http"), out int httpPort) ? httpPort : 8080;
        int photon = int.TryParse(Tunnel.Option(options, "photon"), out int photonPort) ? photonPort : 5055;
        try
        {
            Tunnel.DialAsync(http).Result.Dispose();
        }
        catch (Exception)
        {
            Console.WriteLine($"error no server on port {http}");
            return 2;
        }
        var host = new TunnelHost(Tunnel.Rendezvous(options), secret, info, http, photon);
        Tunnel.ExitWithParent(options, host._stopped.Set);
        if (options.Contains("--public"))
            Announcer.Start(Tunnel.Option(options, "list") ?? Announcer.DefaultList,
                () => new { secret, info = JsonNode.Parse(info) }, () => new { secret, left = true });
        host.Serve();
        Announcer.Leave();
        return 0;
    }

    void Serve()
    {
        _manager.Start();
        for (long tick = 0; !_stopped.IsSet; tick++)
        {
            if (_server is not { ConnectionState: ConnectionState.Connected } server)
                Dial();
            else if (tick % PunchEvery == 0 && _code is string code)
                _manager.NatPunchModule.SendNatIntroduceRequest(new IPEndPoint(server.Address, server.Port), "h:" + code);
            _stopped.Wait(TickMs);
        }
        _manager.Stop();
    }

    void Dial()
    {
        foreach (IPEndPoint endPoint in _rendezvous)
            if (!_dialing.TryGetValue(endPoint, out NetPeer? peer) || peer.ConnectionState == ConnectionState.Disconnected)
                _dialing[endPoint] = _manager.Connect(endPoint, Tunnel.Key);
    }

    void Request(ConnectionRequest request)
    {
        if (_code is string code && _direct.Count + _relayed.Count < MaxGuests) request.AcceptIfKey(code);
        else request.Reject();
    }

    void Connected(NetPeer peer)
    {
        if (Tunnel.IsAny(peer, _rendezvous))
        {
            lock (_sync)
            {
                if (_server is { ConnectionState: ConnectionState.Connected } && _server != peer)
                {
                    peer.Disconnect();
                    return;
                }
                _server = peer;
            }
            Console.WriteLine($"rendezvous {peer.Address}:{peer.Port}");
            Tunnel.Send(peer, Op.Register, _secret, _info);
            return;
        }
        _direct[peer] = new Guest(new Link(peer), _http, _photon);
        Count();
    }

    void Disconnected(NetPeer peer)
    {
        if (Tunnel.IsAny(peer, _rendezvous))
        {
            lock (_sync)
            {
                if (peer != _server) return;
                _server = null;
            }
            foreach (Guest guest in _relayed.Values) guest.Close();
            _relayed.Clear();
            Count();
            return;
        }
        if (_direct.TryRemove(peer, out Guest? gone)) gone.Close();
        Count();
    }

    void Receive(NetPeer peer, NetPacketReader reader, byte lane, DeliveryMethod method)
    {
        try
        {
            if (reader.AvailableBytes == 0) return;
            if (!Tunnel.IsAny(peer, _rendezvous))
            {
                if (_direct.TryGetValue(peer, out Guest? guest)) guest.Receive(reader.GetRemainingBytesSpan());
                return;
            }
            if (peer != _server) return;
            switch ((Op)reader.PeekByte())
            {
                case Op.Registered:
                    reader.GetByte();
                    _code = reader.GetString(16);
                    Console.WriteLine($"code {_code}");
                    _manager.NatPunchModule.SendNatIntroduceRequest(new IPEndPoint(peer.Address, peer.Port), "h:" + _code);
                    break;
                case Op.Missing:
                    Console.WriteLine("error rendezvous refused");
                    _stopped.Set();
                    break;
                case Op.Relayed when reader.AvailableBytes >= 5:
                    int id = Tunnel.IdOf(reader.GetRemainingBytesSpan());
                    _relayed[id] = new Guest(new Link(peer, id), _http, _photon);
                    Count();
                    break;
                case Op.Wrap when reader.AvailableBytes >= 5:
                    ReadOnlySpan<byte> wrapped = reader.GetRemainingBytesSpan();
                    if (_relayed.TryGetValue(Tunnel.IdOf(wrapped), out Guest? relayed)) relayed.Receive(wrapped[5..]);
                    break;
                case Op.Unrelay when reader.AvailableBytes >= 5:
                    if (_relayed.TryRemove(Tunnel.IdOf(reader.GetRemainingBytesSpan()), out Guest? left)) left.Close();
                    Count();
                    break;
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"tunnel: {error.Message}");
        }
    }

    void Count()
    {
        int guests = _direct.Count + _relayed.Count;
        if (Interlocked.Exchange(ref _guests, guests) != guests) Console.WriteLine($"players {guests}");
    }

    public void OnNatIntroductionRequest(IPEndPoint inside, IPEndPoint outside, string token)
    {
    }

    public void OnNatIntroductionSuccess(IPEndPoint target, NatAddressType type, string token)
    {
    }

    sealed class Guest(Link link, int http, int photon)
    {
        const int MaxPipes = 64;
        const int MaxFlows = 8;
        const long FlowIdleMs = 30_000;

        sealed class Flow(Socket socket)
        {
            public Socket Socket => socket;
            public long Seen = Environment.TickCount64;
        }

        readonly ConcurrentDictionary<int, Pipe> _pipes = new();
        readonly ConcurrentDictionary<int, Flow> _flows = new();
        int _closed;

        public void Receive(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 5 || _closed == 1) return;
            int id = Tunnel.IdOf(frame);
            switch ((Op)frame[0])
            {
                case Op.Open:
                    var pipe = new Pipe(id, link);
                    if (_pipes.Count < MaxPipes && _pipes.TryAdd(id, pipe))
                        _ = pipe.RunAsync(Tunnel.DialAsync(http), () => _pipes.TryRemove(id, out _));
                    else
                        _ = pipe.RunAsync(Task.FromException<Socket>(new IOException("too many streams")), () => { });
                    break;
                case Op.Data when _pipes.TryGetValue(id, out Pipe? open):
                    open.Deliver(frame[5..]);
                    break;
                case Op.Close when _pipes.TryGetValue(id, out Pipe? ending):
                    ending.End();
                    break;
                case Op.Credit when frame.Length >= 9 && _pipes.TryGetValue(id, out Pipe? granted):
                    granted.Grant(frame[5..]);
                    break;
                case Op.Datagram when FlowFor(id) is Flow flow:
                    flow.Seen = Environment.TickCount64;
                    flow.Socket.Send(frame[5..]);
                    break;
            }
        }

        Flow? FlowFor(int id)
        {
            if (_flows.TryGetValue(id, out Flow? known)) return known;
            long now = Environment.TickCount64;
            foreach ((int idle, Flow old) in _flows.Where(pair => now - pair.Value.Seen > FlowIdleMs).ToList())
                if (_flows.TryRemove(idle, out _)) old.Socket.Dispose();
            if (_flows.Count >= MaxFlows) return null;
            Socket socket = Tunnel.Udp(new IPEndPoint(Tunnel.Address, 0));
            socket.Connect(IPAddress.Loopback, photon);
            var flow = new Flow(socket);
            if (!_flows.TryAdd(id, flow))
            {
                socket.Dispose();
                return _flows.GetValueOrDefault(id);
            }
            _ = PumpAsync(id, flow);
            return flow;
        }

        async Task PumpAsync(int id, Flow flow)
        {
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    int read = await flow.Socket.ReceiveAsync(buffer, SocketFlags.None);
                    flow.Seen = Environment.TickCount64;
                    link.SendDatagram(id, buffer.AsSpan(0, read));
                }
            }
            catch (Exception)
            {
            }
            _flows.TryRemove(new KeyValuePair<int, Flow>(id, flow));
            flow.Socket.Dispose();
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            foreach (Pipe pipe in _pipes.Values) pipe.End();
            foreach (Flow flow in _flows.Values) flow.Socket.Dispose();
            _flows.Clear();
        }
    }
}
