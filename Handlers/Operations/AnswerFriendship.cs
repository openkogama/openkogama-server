using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class AnswerFriendship(Session session, bool accept) : IOperationHandler
{
    public byte Code => (byte)(accept ? OperationCode.RequestAcceptFriendship : OperationCode.RequestRejectFriendship);

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int id = Convert.ToInt32(request[(byte)ParameterKey.FriendID]);
        Friendship? friendship = Stores.Friends.Find(id);

        if (session.For(peer) is not Player player || friendship is null
            || friendship.Friend != player.ProfileId || friendship.Status != FriendStatus.Pending)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        FriendStatus status = accept ? FriendStatus.Accepted : FriendStatus.Deleted;
        if (accept) Stores.Friends.SetStatus(id, status);
        else Stores.Friends.Remove(id);

        peer.Send(new OperationResponse(request));

        var evt = new EventData((byte)EventCode.FriendUpdate)
        {
            Parameters =
            {
                [(byte)ParameterKey.FriendID] = id,
                [(byte)ParameterKey.ProfileID] = friendship.Profile,
                [(byte)ParameterKey.FriendStatusID] = (int)status,
            },
        };
        foreach (Player other in session.Players)
            if (other.ProfileId == friendship.Profile || other.ProfileId == friendship.Friend)
                other.Peer.Send(evt);
    }
}
