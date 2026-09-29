using OpenKogama.Api;
using OpenKogama.Game;

namespace OpenKogama.Plugins;

sealed class PluginWorld(Session session) : IWorld
{
    public int Id => session.WorldId ?? 0;
    public string Name => session.Name;
    public bool Play => session.Play;
    public IReadOnlyList<IPlayer> Players => [.. session.Players.Select(player => PluginHost.PlayerOf(session, player))];

    public void Broadcast(string text, string? color = null)
    {
        foreach (Player player in session.Players)
            ServerChat.Message(player, text, color);
    }
}
