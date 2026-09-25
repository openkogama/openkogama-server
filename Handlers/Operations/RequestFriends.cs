using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestFriends(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestFriends;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        PhotonDictionary friends = PhotonDictionary.Untyped();
        int profile = session.For(peer)?.ProfileId ?? 0;

        foreach (Friendship friendship in Stores.Friends.Friends(profile))
        {
            bool mine = friendship.Profile == profile || friendship.Status == FriendStatus.Accepted;
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.ProfileID, mine ? profile : friendship.Profile);
            entry.Add((byte)DBQueryKey.FriendProfileID, mine ? Other(friendship, profile) : profile);
            entry.Add((byte)DBQueryKey.FriendStatus, (int)friendship.Status);
            friends.Add(friendship.Id, entry);
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.Friends] = friends },
        });
    }

    static int Other(Friendship friendship, int profile) =>
        friendship.Profile == profile ? friendship.Friend : friendship.Profile;
}
