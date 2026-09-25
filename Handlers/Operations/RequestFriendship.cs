using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestFriendship(Session session, bool byName) : IOperationHandler
{
    const short AlreadyRequestedByOther = -6;
    const short AlreadyFriends = -4;
    const short AlreadyPending = -3;
    const short NoSuchUser = -2;
    const short Failed = -1;

    public byte Code => (byte)(byName ? OperationCode.RequestFriendshipByName : OperationCode.RequestFriendshipByProfileID);

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = Failed });
            return;
        }

        int? target = byName
            ? session.Players.FirstOrDefault(other => other.Username == request[(byte)ParameterKey.Username] as string)?.ProfileId
            : Convert.ToInt32(request[(byte)ParameterKey.FriendProfileID]);

        short code = Check(player.ProfileId, target);
        if (code != 0)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = code });
            return;
        }

        Friendship friendship = Stores.Friends.Request(player.ProfileId, target!.Value);
        peer.Send(new OperationResponse(request));

        var evt = new EventData((byte)EventCode.FriendRequest)
        {
            Parameters =
            {
                [(byte)ParameterKey.FriendID] = friendship.Id,
                [(byte)ParameterKey.ProfileID] = friendship.Profile,
                [(byte)ParameterKey.FriendProfileID] = friendship.Friend,
            },
        };
        foreach (Player other in session.Players)
            if (other.ProfileId == friendship.Profile || other.ProfileId == friendship.Friend)
                other.Peer.Send(evt);
    }

    static short Check(int profile, int? target)
    {
        if (target is not int other) return NoSuchUser;
        if (other == profile) return Failed;

        return Stores.Friends.Between(profile, other) switch
        {
            null => 0,
            { Status: FriendStatus.Accepted } => AlreadyFriends,
            { Profile: var from } when from == profile => AlreadyPending,
            _ => AlreadyRequestedByOther,
        };
    }
}
