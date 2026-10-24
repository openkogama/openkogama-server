using System.Numerics;
using System.Runtime.CompilerServices;
using OpenKogama.Game;
using OpenKogama.World;

namespace OpenKogama.Plugins;

static class TeleporterPads
{
    const float Reach = 1.25f;
    const float Below = 1f;
    const float Above = 2.5f;
    const long RefreshMs = 1000;

    sealed class Pads
    {
        public long At = long.MinValue;
        public List<(WorldObject Pad, Vector3 Center)> All = [];
    }

    static readonly ConditionalWeakTable<Session, Pads> Known = new();
    static readonly ConditionalWeakTable<Player, HashSet<int>> Standing = new();

    public static void Watch(Session session, Action<Session, Player, WorldObject, bool> triggered)
    {
        if (!session.Play) return;
        Pads pads = Known.GetOrCreateValue(session);
        long now = Environment.TickCount64;
        if (now - pads.At >= RefreshMs)
        {
            pads.At = now;
            pads.All = [.. session.World.FindAll(WorldObjectType.Teleporter).Select(pad => (pad, session.World.WorldMatrix(pad.Id).Translation))];
        }

        foreach (Player player in session.Players.ToList())
        {
            if (!player.InWorld || player.ReportsTeleporters || player.LastPosition is not float[] at) continue;
            var position = new Vector3(at[0], at[1], at[2]);
            HashSet<int> standing = Standing.GetOrCreateValue(player);
            foreach ((WorldObject pad, Vector3 center) in pads.All)
            {
                Vector3 offset = position - center;
                bool on = new Vector2(offset.X, offset.Z).Length() <= Reach && offset.Y >= -Below && offset.Y <= Above;
                if (on == standing.Contains(pad.Id)) continue;
                if (on) standing.Add(pad.Id);
                else standing.Remove(pad.Id);
                triggered(session, player, pad, on);
            }
        }
    }
}
