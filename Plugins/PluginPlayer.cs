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

    public int AddXp(int amount) => Experience.Award(player, amount, Experience.NoReason);

    public void Message(string text, string? color = null) => ServerChat.Message(player, text, color);

    public void Say(string text) => ServerChat.Say(session, player, text);

    public void Kick() => player.Peer.Disconnect();
}
