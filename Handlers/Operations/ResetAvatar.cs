using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class ResetAvatar(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ResetAvatar;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        if (session.For(peer) is not Player player || !session.BodyAvatars.ContainsKey(bodyId))
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Snapshot body = session.ResetEditorBody(bodyId, player.Actor);

        peer.Send(new OperationResponse(request));
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = WorldSerializer.Write(body),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
            },
        });
        peer.Send(new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = bodyId },
        });
    }
}
