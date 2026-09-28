using OpenKogama.Api;

namespace Xp;

public sealed class XpPlugin : IPlugin
{
    const int FirstReward = 2;
    const int MaxReward = 1 << 30;

    readonly Dictionary<int, int> _next = [];

    public void Load(IServer server) => server.Chat += OnChat;

    void OnChat(ChatMessage message)
    {
        if (!message.Text.Trim().Equals("xp", StringComparison.OrdinalIgnoreCase)) return;

        message.Cancel = true;
        int profile = message.Player.ProfileId;
        int amount = _next.GetValueOrDefault(profile, FirstReward);
        if (amount < MaxReward) _next[profile] = amount * 2;

        int total = message.Player.AddXp(amount);
        message.Player.Message($"+{amount} XP ({total} total)");
    }
}
