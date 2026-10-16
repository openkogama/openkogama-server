using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LiteNetLib;
using LiteNetLib.Utils;

namespace OpenKogama.P2p;

public static partial class Tunnel
{
    public const string Key = "openkogama-p2p";
    public const int DefaultPort = 5058;
    public const byte Lanes = 8;
    public const int CodeLength = 6;
    public static readonly IPAddress Address = IPAddress.Parse("127.0.0.2");
    public static readonly IPAddress Address2012 = IPAddress.Parse("127.0.0.3");
    public const int Photon2012 = 5055;

    const string DefaultRendezvous = "p2p-eu.openkogama.org,p2p-na.openkogama.org,p2p-sa.openkogama.org,p2p-as.openkogama.org";
    const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const int SioUdpConnReset = -1744830452;

    public static bool Carries(IPAddress address) => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).Equals(Address);

    public static string? Via(string? host) => host is not null && LoopbackHost().IsMatch(host) ? host : null;

    public static int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["host", .. var options] => TunnelHost.Run(options),
                ["join", var code, .. var options] => TunnelJoin.Run(code, options, findOnly: false),
                ["find", var code, .. var options] => TunnelJoin.Run(code, options, findOnly: true),
                _ => Usage(),
            };
        }
        catch (SocketException)
        {
            Console.WriteLine("error rendezvous unknown");
            return 3;
        }
    }

    static int Usage()
    {
        Console.WriteLine("usage: tunnel host [--secret S] [--info JSON] [--http 8080] [--photon 5055]");
        Console.WriteLine("       tunnel join CODE [--relay]");
        Console.WriteLine("       tunnel find CODE");
        Console.WriteLine("each takes [--rendezvous host:port] [--parent PID]");
        return 1;
    }

    internal static string? Option(string[] options, string name)
    {
        int at = Array.IndexOf(options, "--" + name);
        return at >= 0 && at + 1 < options.Length ? options[at + 1] : null;
    }

    internal static IPEndPoint[] Rendezvous(string[] options)
    {
        string[] addresses = (Option(options, "rendezvous") ?? DefaultRendezvous)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IPEndPoint?[] found = Task.WhenAll(addresses.Select(ResolveAsync)).Result;
        IPEndPoint[] usable = [.. found.OfType<IPEndPoint>().Distinct()];
        return usable.Length > 0 ? usable : throw new SocketException((int)SocketError.HostNotFound);
    }

    static async Task<IPEndPoint?> ResolveAsync(string address)
    {
        int colon = address.LastIndexOf(':');
        int port = colon > 0 && int.TryParse(address[(colon + 1)..], out int given) ? given : DefaultPort;
        string host = colon > 0 ? address[..colon] : address;
        if (IPAddress.TryParse(host, out IPAddress? literal)) return new IPEndPoint(literal, port);
        try
        {
            IPAddress[] all = await Dns.GetHostAddressesAsync(host);
            return all.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork) is IPAddress ip
                ? new IPEndPoint(ip, port) : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    internal static bool IsAny(NetPeer peer, IPEndPoint[] endPoints) => endPoints.Any(endPoint => Is(peer, endPoint));

    internal static NetManager Manager(EventBasedNetListener events, INatPunchListener? punch = null)
    {
        var manager = new NetManager(events)
        {
            ChannelsCount = Lanes,
            UnsyncedEvents = true,
            AutoRecycle = true,
            IPv6Enabled = false,
            MtuDiscovery = true,
            DisconnectTimeout = 10_000,
            UpdateTime = 5,
            NatPunchEnabled = punch is not null,
        };
        if (punch is not null)
        {
            manager.NatPunchModule.UnsyncedEvents = true;
            manager.NatPunchModule.Init(punch);
        }
        return manager;
    }

    internal static bool Is(NetPeer peer, IPEndPoint endPoint) => peer.Port == endPoint.Port && peer.Address.Equals(endPoint.Address);

    internal static string CodeOf(string secret)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Key + ":" + secret));
        return string.Concat(hash.Take(CodeLength).Select(value => Letters[value % Letters.Length]));
    }

    internal static string RandomCode() =>
        string.Concat(Enumerable.Range(0, CodeLength).Select(_ => Letters[RandomNumberGenerator.GetInt32(Letters.Length)]));

    internal static string? Normalize(string? code)
    {
        if (code is null) return null;
        string clean = string.Concat(code.ToUpperInvariant().Where(letter => letter is not ('-' or ' ')));
        return clean.Length == CodeLength && clean.All(Letters.Contains) ? clean : null;
    }

    internal static byte[] Frame(Op op, int id, ReadOnlySpan<byte> payload = default)
    {
        var frame = new byte[5 + payload.Length];
        frame[0] = (byte)op;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), id);
        payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    internal static int IdOf(ReadOnlySpan<byte> frame) => BinaryPrimitives.ReadInt32LittleEndian(frame[1..]);

    internal static void Send(NetPeer peer, Op op, params string[] fields)
    {
        var writer = new NetDataWriter();
        writer.Put((byte)op);
        foreach (string field in fields) writer.Put(field);
        peer.Send(writer, 0, DeliveryMethod.ReliableOrdered);
    }

    internal static Socket Udp(IPEndPoint local)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows()) socket.IOControl(SioUdpConnReset, new byte[4], null);
        socket.Bind(local);
        return socket;
    }

    internal static async Task<Socket> DialAsync(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.Bind(new IPEndPoint(Address, 0));
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
            return socket;
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }
    }

    internal static void ExitWithParent(string[] options, Action exit)
    {
        if (!int.TryParse(Option(options, "parent"), out int id)) return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(2000);
                try
                {
                    using Process parent = Process.GetProcessById(id);
                    if (parent.HasExited) break;
                }
                catch (Exception)
                {
                    break;
                }
            }
            exit();
        });
    }

    [GeneratedRegex(@"^127\.0\.0\.1:[1-9][0-9]{0,4}$")]
    private static partial Regex LoopbackHost();
}
