using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateWorldObject(Session session) : IOperationHandler
{

    public byte Code => (byte)OperationCode.UpdateWorldObject;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? sender = session.For(peer);
        if (sender is not null
            && Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]) == sender.AvatarId
            && request.Parameters.TryGetValue((byte)ParameterKey.PosY, out object? y))
        {
            session.Round.TrackHeight(sender, Convert.ToSingle(y));
        }

        var evt = new EventData((byte)EventCode.UpdateWorldObject)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        foreach (Player player in session.Players)
            if (player.Peer != peer)
                player.Peer.Send(evt, reliable: false);
    }
}
