using System.Net;
using LiteNetLib;

namespace OpenKogama.P2p;

public sealed class Rendezvous(int port) : INatPunchListener
{
    const int MinSecret = 16;
    const int MaxSecret = 64;
    const int MaxInfo = 1024;
    const int MaxRelays = 16;
    const int MaxRelayBacklog = 4096;

    sealed class Hosted(NetPeer peer, string secret, string info)
    {
        public NetPeer Peer => peer;
        public string Secret => secret;
        public string Info => info;
        public IPEndPoint? Inside { get; set; }
    }

    sealed class Relaying(NetPeer host, NetPeer guest, string code)
    {
        public NetPeer Host => host;
        public NetPeer Guest => guest;
        public string Code => code;
        public long Bytes;
    }

    readonly object _sync = new();
    readonly Dictionary<string, Hosted> _hosts = [];
    readonly Dictionary<int, Relaying> _relays = [];
    NetManager _manager = null!;
    int _lastRelay;

    public static int Run(string[] options)
    {
        int port = int.TryParse(Tunnel.Option(options, "port"), out int given) ? given : Tunnel.DefaultPort;
        new Rendezvous(port).Serve();
        return 0;
    }

    void Serve()
    {
        var events = new EventBasedNetListener();
        events.ConnectionRequestEvent += request => request.AcceptIfKey(Tunnel.Key);
        events.NetworkReceiveEvent += Receive;
        events.PeerDisconnectedEvent += (peer, _) => Forget(peer);
        _manager = Tunnel.Manager(events, this);
        if (!_manager.Start(port)) throw new InvalidOperationException($"rendezvous cannot listen on udp {port}");
        Console.WriteLine($"rendezvous udp {port}");
        Thread.Sleep(Timeout.Infinite);
    }

    void Receive(NetPeer peer, NetPacketReader reader, byte lane, DeliveryMethod method)
    {
        try
        {
            if (reader.AvailableBytes == 0) return;
            switch ((Op)reader.PeekByte())
            {
                case Op.Register:
                    reader.GetByte();
                    Register(peer, reader.GetString(MaxSecret), reader.GetString(MaxInfo));
                    break;
                case Op.Find:
                    reader.GetByte();
                    Find(peer, Tunnel.Normalize(reader.GetString(16)));
                    break;
                case Op.Relay:
                    reader.GetByte();
                    Relay(peer, Tunnel.Normalize(reader.GetString(16)));
                    break;
                case Op.Wrap when reader.AvailableBytes >= 5:
                    Forward(peer, reader.GetRemainingBytesSpan(), lane, method);
                    break;
                case Op.Unrelay when reader.AvailableBytes >= 5:
                    Unrelay(Tunnel.IdOf(reader.GetRemainingBytesSpan()), peer);
                    break;
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"rendezvous: bad message from {peer.Address}: {error.Message}");
        }
    }

    void Register(NetPeer peer, string secret, string info)
    {
        if (secret.Length < MinSecret)
        {
            Tunnel.Send(peer, Op.Missing);
            return;
        }
        string code = Tunnel.CodeOf(secret);
        lock (_sync)
        {
            foreach (string old in _hosts.Where(pair => pair.Value.Peer == peer).Select(pair => pair.Key).ToList())
                _hosts.Remove(old);
            if (_hosts.TryGetValue(code, out Hosted? taken) && taken.Secret != secret)
                do code = Tunnel.RandomCode(); while (_hosts.ContainsKey(code));
            _hosts[code] = new Hosted(peer, secret, info);
        }
        Tunnel.Send(peer, Op.Registered, code);
        Console.WriteLine($"host {code} from {peer.Address}");
    }

    void Find(NetPeer peer, string? code)
    {
        Hosted? hosted;
        lock (_sync) hosted = code is null ? null : _hosts.GetValueOrDefault(code);
        if (hosted is null) Tunnel.Send(peer, Op.Missing);
        else Tunnel.Send(peer, Op.Found, hosted.Info);
    }

    void Relay(NetPeer guest, string? code)
    {
        int id;
        NetPeer host;
        lock (_sync)
        {
            if (code is null || !_hosts.TryGetValue(code, out Hosted? hosted) || _relays.Count >= MaxRelays)
            {
                Tunnel.Send(guest, Op.Missing);
                return;
            }
            id = ++_lastRelay;
            host = hosted.Peer;
            _relays[id] = new Relaying(host, guest, code);
        }
        byte[] frame = Tunnel.Frame(Op.Relayed, id);
        host.Send(frame, 0, DeliveryMethod.ReliableOrdered);
        guest.Send(frame, 0, DeliveryMethod.ReliableOrdered);
        Console.WriteLine($"relay {id} for {code} from {guest.Address}");
    }

    void Forward(NetPeer from, ReadOnlySpan<byte> frame, byte lane, DeliveryMethod method)
    {
        int id = Tunnel.IdOf(frame);
        Relaying? relay;
        lock (_sync) relay = _relays.GetValueOrDefault(id);
        NetPeer? to = relay is null ? null : relay.Host == from ? relay.Guest : relay.Guest == from ? relay.Host : null;
        if (relay is null || to is null) return;
        if (method == DeliveryMethod.Unreliable && frame.Length > to.GetMaxSinglePacketSize(DeliveryMethod.Unreliable))
            method = DeliveryMethod.ReliableUnordered;
        if (method != DeliveryMethod.Unreliable && to.GetPacketsCountInReliableQueue(lane, method == DeliveryMethod.ReliableOrdered) > MaxRelayBacklog)
        {
            Unrelay(id, null);
            return;
        }
        Interlocked.Add(ref relay.Bytes, frame.Length);
        to.Send(frame, lane, method);
        _manager.TriggerUpdate();
    }

    void Unrelay(int id, NetPeer? from)
    {
        Relaying? relay;
        lock (_sync)
        {
            if (!_relays.TryGetValue(id, out relay) || from is not null && relay.Host != from && relay.Guest != from) return;
            _relays.Remove(id);
        }
        byte[] frame = Tunnel.Frame(Op.Unrelay, id);
        foreach (NetPeer peer in new[] { relay.Host, relay.Guest })
            if (peer != from && peer.ConnectionState == ConnectionState.Connected)
                peer.Send(frame, 0, DeliveryMethod.ReliableOrdered);
        Console.WriteLine($"relay {id} for {relay.Code} closed after {relay.Bytes / 1024} KB");
    }

    void Forget(NetPeer peer)
    {
        List<int> ended;
        lock (_sync)
        {
            foreach (string code in _hosts.Where(pair => pair.Value.Peer == peer).Select(pair => pair.Key).ToList())
            {
                _hosts.Remove(code);
                Console.WriteLine($"host {code} gone");
            }
            ended = [.. _relays.Where(pair => pair.Value.Host == peer || pair.Value.Guest == peer).Select(pair => pair.Key)];
        }
        foreach (int id in ended) Unrelay(id, peer);
    }

    public void OnNatIntroductionRequest(IPEndPoint inside, IPEndPoint outside, string token)
    {
        if (token.Length < 3 || token[1] != ':' || Tunnel.Normalize(token[2..]) is not string code) return;
        Hosted? hosted;
        lock (_sync) hosted = _hosts.GetValueOrDefault(code);
        if (hosted is null) return;
        var hostOutside = new IPEndPoint(hosted.Peer.Address, hosted.Peer.Port);
        if (token[0] == 'h' && hostOutside.Equals(outside)) hosted.Inside = inside;
        else if (token[0] == 'j') _manager.NatPunchModule.NatIntroduce(hosted.Inside ?? hostOutside, hostOutside, inside, outside, code);
    }

    public void OnNatIntroductionSuccess(IPEndPoint target, NatAddressType type, string token)
    {
    }
}
