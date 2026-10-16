using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;
using LiteNetLib;

namespace OpenKogama.P2p;

sealed class Pipe(int id, Link link)
{
    const int Chunk = 16 * 1024;
    const int Window = 256 * 1024;
    const int GrantEvery = Window / 4;
    const int MaxBacklog = 96;

    readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    readonly SemaphoreSlim _granted = new(0);
    int _credit = Window;
    int _consumed;
    int _closed;

    byte Lane => (byte)(1 + id % (Tunnel.Lanes - 1));

    public void Open() => link.Send(Tunnel.Frame(Op.Open, id), Lane, DeliveryMethod.ReliableOrdered);

    public void Deliver(ReadOnlySpan<byte> data) => _incoming.Writer.TryWrite(data.ToArray());

    public void End() => _incoming.Writer.TryComplete();

    public void Grant(ReadOnlySpan<byte> amount)
    {
        Interlocked.Add(ref _credit, BinaryPrimitives.ReadInt32LittleEndian(amount));
        _granted.Release();
    }

    public async Task RunAsync(Task<Socket> connecting, Action finished)
    {
        try
        {
            using Socket socket = await connecting;
            Task writing = WriteAsync(socket);
            await ReadAsync(socket);
            Close();
            await writing;
        }
        catch (Exception)
        {
            Close();
        }
        finally
        {
            finished();
        }
    }

    async Task ReadAsync(Socket socket)
    {
        var buffer = new byte[Chunk];
        try
        {
            while (await socket.ReceiveAsync(buffer, SocketFlags.None) is int read and > 0)
            {
                while (link.Alive && (Volatile.Read(ref _credit) < read || link.Backlog(Lane) > MaxBacklog))
                    await _granted.WaitAsync(5);
                if (!link.Alive) return;
                Interlocked.Add(ref _credit, -read);
                link.Send(Tunnel.Frame(Op.Data, id, buffer.AsSpan(0, read)), Lane, DeliveryMethod.ReliableOrdered);
            }
        }
        catch (Exception)
        {
        }
    }

    async Task WriteAsync(Socket socket)
    {
        try
        {
            await foreach (byte[] data in _incoming.Reader.ReadAllAsync())
            {
                for (int sent = 0; sent < data.Length;)
                    sent += await socket.SendAsync(data.AsMemory(sent), SocketFlags.None);
                if (Interlocked.Add(ref _consumed, data.Length) >= GrantEvery)
                {
                    var amount = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(amount, Interlocked.Exchange(ref _consumed, 0));
                    link.Send(Tunnel.Frame(Op.Credit, id, amount), Lane, DeliveryMethod.ReliableOrdered);
                }
            }
        }
        catch (Exception)
        {
        }
        socket.Dispose();
    }

    void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0 && link.Alive)
            link.Send(Tunnel.Frame(Op.Close, id), Lane, DeliveryMethod.ReliableOrdered);
    }
}
