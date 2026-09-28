using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Handlers.Legacy;
using OpenKogama.Handlers.Legacy2012;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Plugins;

sealed class PluginPlayer(Player player) : IPlayer
{
    public int ProfileId => player.ProfileId;
    public string Name => player.Username;

    public int AddXp(int amount) => Experience.Award(player, amount, Experience.NoReason);

    public void Message(string text)
    {
        if (player.Peer.Protocol == PhotonProtocol.Protocol15)
        {
            player.Peer.Send(new EventData((byte)Event2012.ChatMsg)
            {
                Parameters = { [(byte)Key2012.ActorNr] = player.Actor, [(byte)Key2012.ChatMsg] = text },
            });
            return;
        }

        player.Peer.Send(new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = SendChatMsg.ChatMessage,
                [(byte)ParameterKey.GameMsgData] = new Dictionary<object, object?> { [SendChatMsg.Sender] = player.Actor, [SendChatMsg.Text] = text },
            },
        });
    }
}
