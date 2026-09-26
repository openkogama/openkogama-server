using OpenKogama.Game;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public interface ILegacyHandler
{
    string Operation { get; }
    void Handle(PhotonPeer peer, OperationRequest request, LegacyTranslator protocol, Session session);
}
