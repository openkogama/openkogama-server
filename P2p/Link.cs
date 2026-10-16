using LiteNetLib;

namespace OpenKogama.P2p;

sealed class Link(NetPeer peer, int relay = 0)
{
    const int WrapSize = 5;

    public NetPeer Peer => peer;
    public int Relay => relay;
    public bool Alive => peer.ConnectionState == ConnectionState.Connected;

    public void Send(byte[] frame, byte lane, DeliveryMethod method)
    {
        peer.Send(relay == 0 ? frame : Tunnel.Frame(Op.Wrap, relay, frame), lane, method);
        peer.NetManager.TriggerUpdate();
    }

    public int Backlog(byte lane) => peer.GetPacketsCountInReliableQueue(lane, true);

    public void SendDatagram(int flow, ReadOnlySpan<byte> data)
    {
        byte[] frame = Tunnel.Frame(Op.Datagram, flow, data);
        int limit = peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable) - (relay == 0 ? 0 : WrapSize);
        Send(frame, 0, frame.Length <= limit ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableUnordered);
    }
}
