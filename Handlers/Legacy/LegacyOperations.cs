using OpenKogama.Game;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public static class LegacyOperations
{
    static readonly Dictionary<string, ILegacyHandler> Handlers = new ILegacyHandler[]
    {
        new RequestTeamList(),
        new SendChatMsg(),
    }.ToDictionary(handler => handler.Operation);

    public static bool Handle(PhotonPeer peer, OperationRequest request, LegacyTranslator protocol, Session session)
    {
        string name = protocol.ClientOperation(request.OperationCode);
        if (!Handlers.TryGetValue(name, out ILegacyHandler? handler))
        {
            Console.WriteLine($"peer {peer.Id}: no {protocol.Version} handler for {name}");
            return false;
        }

        handler.Handle(peer, request, protocol, session);
        return true;
    }
}
