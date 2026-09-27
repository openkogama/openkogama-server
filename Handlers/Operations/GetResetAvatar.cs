using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class GetResetAvatar(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetResetAvatar;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (session.For(peer) is not Player player || !session.BodyAvatars.TryGetValue(bodyId, out int avatar))
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        byte[] data = Stores.Profiles.AvatarSource(avatar) is int source && AvatarShop.Find(source) is { } original
            ? original.Bytes
            : Avatar.DefaultBody(player.Actor);

        peer.Send(new OperationResponse(request));
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = data,
                [(byte)ParameterKey.QueryType] = (byte)QueryType.Item,
                [(byte)ParameterKey.QueryId] = GetNextGameBatch.NextQueryId(),
                [(byte)ParameterKey.QueryDataLeft] = false,
            },
        });
    }
}
