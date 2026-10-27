using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetThemesData(Game.Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetThemesData;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.MetaData] = Game.Themes.Catalog(session.For(peer) is { } player && Game.WorldTheme.KnowsCustom(player)) },
        });
}
