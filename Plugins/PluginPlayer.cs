using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Plugins;

sealed class PluginPlayer(Player player, Session session) : IPlayer
{
    public int Actor => player.Actor;
    public int ProfileId => player.ProfileId;
    public string Name => player.Username;
    public string ClientVersion => player.Build;
    public IWorld World => PluginHost.WorldOf(session);
    public int Xp => Stores.Profiles.Xp(player.ProfileId);
    public int Gold => Stores.Profiles.Gold(player.ProfileId);
    public AvatarSkin Skin => AvatarFiles.Export(Stores.Profiles.ActiveAvatar(player.ProfileId)) ?? new AvatarSkin([], []);

    public Vector3? Position => player.LastPosition is float[] position ? new Vector3(position[0], position[1], position[2]) : null;

    public int AddXp(int amount) => Experience.Award(player, amount, Experience.NoReason);

    public int AddGold(int amount) => Stores.Profiles.AddGold(player.ProfileId, amount);

    public void Message(string text, string? color = null) => ServerChat.Message(player, text, color);

    public void Say(string text) => ServerChat.Say(session, player, text);

    public void Notify(string text, bool brief = false) => ServerChat.Notify(player, text, brief);

    public void Announce(string text, string? icon = null) => session.Announce(text, icon, [player]);

    public void Kick() => player.Peer.Disconnect();

    public void Damage(float amount, INpc? by = null, Vector3? impulse = null) =>
        session.DamagePlayer(player, by?.Id ?? player.Actor, amount, impulse ?? Vector3.Zero, by is null ? KilledBy.Environmental : KilledBy.Mutant);
}
