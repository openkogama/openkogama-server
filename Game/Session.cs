using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed class Session
{
    readonly List<Player> _players = [];
    int _nextAvatarId = 10;

    public IReadOnlyList<Player> Players => _players;

    // Each avatar subtree takes a block of ids: avatar, body, and one per bone.
    public Player Add(PhotonPeer peer)
    {
        Player player = new(peer, peer.Id, _nextAvatarId);
        _nextAvatarId += 2 + Avatar.Parts.Count;
        _players.Add(player);
        return player;
    }

    public Player? For(PhotonPeer peer) => _players.FirstOrDefault(p => p.Peer == peer);

    public void Remove(PhotonPeer peer) => _players.RemoveAll(p => p.Peer == peer);
}
