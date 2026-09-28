using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class LevelChanged(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.LevelChanged;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? player = session.For(peer);
        if (player is null) return;

        player.Level = Convert.ToInt32(request[(byte)ParameterKey.Level]);

        var evt = new EventData((byte)EventCode.LevelChanged)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Level] = player.Level,
            },
        };
        foreach (Player other in session.Players)
            if (other != player && other.Saw(player.Actor))
                other.Peer.Send(evt);
    }
}
