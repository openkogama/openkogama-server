using OpenKogama.Game;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public sealed class LegacyRelay(string operation, string eventName) : ILegacyHandler
{
    public string Operation => operation;

    public void Handle(PhotonPeer peer, OperationRequest request, LegacyTranslator protocol, Session session)
    {
        foreach (Player player in session.Players)
            if (player.Peer.Translator is LegacyTranslator other && other.HasEvent(eventName))
                other.SendRaw(player.Peer, new EventData(other.Event(eventName)) { Parameters = new Dictionary<byte, object?>(request.Parameters) });
    }
}
