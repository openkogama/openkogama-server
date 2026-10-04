using System.Numerics;
using OpenKogama.Api;

namespace Deadzone;

sealed class NpcTest(AvatarSkin zombieSkin)
{
    const float FollowDistance = 2.5f;
    const float Spacing = 1.5f;
    const float Settle = 0.6f;
    const int Relaxation = 4;
    const long RespawnMs = 5000;
    const float GoldenAngle = 2.39996f;

    readonly List<(INpc Npc, IPlayer Leader)> _following = [];
    readonly Dictionary<int, Vector3> _homes = [];
    readonly List<(INpc Npc, long At)> _respawns = [];
    readonly object _sync = new();

    public void Command(IPlayer player, string[] arguments)
    {
        switch (arguments.FirstOrDefault()?.ToLowerInvariant())
        {
            case "follow":
                Follow(player);
                break;
            case "clear":
                Clear(player);
                break;
            case "jump":
                foreach (INpc npc in player.World.Npcs) npc.Jump();
                break;
            default:
                Spawn(player);
                break;
        }
    }

    public void Killed(INpc npc)
    {
        lock (_sync) _respawns.Add((npc, Environment.TickCount64 + RespawnMs));
    }

    public void Update()
    {
        RespawnDue();
        (INpc Npc, IPlayer Leader)[] following;
        lock (_sync)
        {
            _following.RemoveAll(entry => !entry.Npc.Exists);
            following = [.. _following];
        }

        foreach (IGrouping<int, (INpc Npc, IPlayer Leader)> group in following.GroupBy(entry => entry.Leader.ProfileId))
        {
            IPlayer leader = group.First().Leader;
            if (leader.Position is not Vector3 center) continue;
            INpc[] npcs = [.. group.Select(entry => entry.Npc)];
            HashSet<int> members = [.. npcs.Select(npc => npc.Id)];
            Vector3[] obstacles = [.. leader.World.Npcs.Where(other => !members.Contains(other.Id)).Select(other => other.Position)];
            Vector3[] targets = Targets(center, npcs, obstacles);

            for (int i = 0; i < npcs.Length; i++)
            {
                Vector3 left = targets[i] - npcs[i].Position;
                left.Y = 0f;
                if (left.Length() > Settle) npcs[i].WalkTo(targets[i]);
                else if (npcs[i].Walking) npcs[i].Stop();
            }
        }
    }

    void RespawnDue()
    {
        List<(INpc Npc, long At)> due;
        lock (_sync)
        {
            long now = Environment.TickCount64;
            due = [.. _respawns.Where(entry => entry.At <= now)];
            _respawns.RemoveAll(entry => entry.At <= now);
        }
        foreach ((INpc npc, _) in due)
            if (npc.Exists && _homes.TryGetValue(npc.Id, out Vector3 home)) npc.Respawn(home);
    }

    static Vector3[] Targets(Vector3 center, INpc[] npcs, Vector3[] obstacles)
    {
        var targets = new Vector3[npcs.Length];
        for (int i = 0; i < npcs.Length; i++)
            targets[i] = center + Direction(npcs[i].Position - center, i) * FollowDistance;

        for (int round = 0; round < Relaxation; round++)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                for (int j = i + 1; j < targets.Length; j++)
                {
                    Vector3 apart = targets[i] - targets[j];
                    apart.Y = 0f;
                    float gap = apart.Length();
                    if (gap >= Spacing) continue;
                    Vector3 push = Direction(apart, i + j) * ((Spacing - gap) / 2f);
                    targets[i] += push;
                    targets[j] -= push;
                }

                foreach (Vector3 obstacle in obstacles)
                {
                    Vector3 apart = targets[i] - obstacle;
                    apart.Y = 0f;
                    float gap = apart.Length();
                    if (gap < Spacing) targets[i] += Direction(apart, i) * (Spacing - gap);
                }

                Vector3 fromLeader = targets[i] - center;
                fromLeader.Y = 0f;
                if (fromLeader.Length() < FollowDistance) targets[i] = center + Direction(fromLeader, i) * FollowDistance;
            }
        }
        return targets;
    }

    static Vector3 Direction(Vector3 value, int salt)
    {
        value.Y = 0f;
        float length = value.Length();
        if (length > 0.001f) return value / length;
        float angle = salt * GoldenAngle;
        return new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
    }

    void Spawn(IPlayer player)
    {
        if (player.Position is not Vector3 position)
        {
            player.Message("Move a bit first");
            return;
        }
        INpc npc = player.World.SpawnNpc("Zombie", position + new Vector3(2, 0, 0), -90f, zombieSkin);
        lock (_sync) _homes[npc.Id] = position + new Vector3(2, 0, 0);
        player.Message($"Zombie {npc.Id} spawned, /npc follow, /npc jump, /npc clear");
    }

    void Follow(IPlayer player)
    {
        lock (_sync)
        {
            List<INpc> stopped = [.. _following.Where(entry => entry.Leader.ProfileId == player.ProfileId).Select(entry => entry.Npc)];
            if (stopped.Count > 0)
            {
                _following.RemoveAll(entry => entry.Leader.ProfileId == player.ProfileId);
                foreach (INpc npc in stopped) npc.Stop();
                player.Message("NPCs stopped following you");
                return;
            }
            foreach (INpc npc in player.World.Npcs) _following.Add((npc, player));
        }
        player.Message("NPCs follow you now");
    }

    void Clear(IPlayer player)
    {
        foreach (INpc npc in player.World.Npcs) npc.Remove();
        lock (_sync) _following.RemoveAll(entry => !entry.Npc.Exists);
        player.Message("NPCs removed");
    }
}
