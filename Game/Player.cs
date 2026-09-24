using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed class Player(PhotonPeer peer, int actor, int avatarId)
{
    public PhotonPeer Peer => peer;
    public int Actor => actor;
    public int AvatarId => avatarId;

    public int ProfileId { get; set; } = actor;
    public Team Team { get; set; }
    public int Level { get; set; } = 1;
    public string Username => $"Player{actor}";
    public string Region => "en_US";
}
