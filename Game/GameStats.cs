using OpenKogama.Kogama;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class GameStats
{
    sealed class TeamCounter
    {
        public int TeamCount;
        public readonly Dictionary<int, int> Actors = [];
    }

    readonly Dictionary<GameStatCounterType, Dictionary<Team, TeamCounter>> _counters = [];
    readonly object _sync = new();

    public int Increment(GameStatCounterType type, Team team, int actor, int value, bool includeTeamScore)
    {
        lock (_sync)
        {
            if (!_counters.TryGetValue(type, out var teams)) _counters[type] = teams = [];
            if (!teams.TryGetValue(team, out TeamCounter? counter)) teams[team] = counter = new();

            if (includeTeamScore) counter.TeamCount += value;
            counter.Actors[actor] = counter.Actors.GetValueOrDefault(actor) + value;
            return counter.Actors[actor];
        }
    }

    public void Set(GameStatCounterType type, Team team, int actor, int value, bool higherIsBetter)
    {
        lock (_sync)
        {
            if (!_counters.TryGetValue(type, out var teams)) _counters[type] = teams = [];
            if (!teams.TryGetValue(team, out TeamCounter? counter)) teams[team] = counter = new();

            counter.Actors[actor] = value;
            counter.TeamCount = higherIsBetter ? counter.Actors.Values.Max() : counter.Actors.Values.Min();
        }
    }

    public int? Get(GameStatCounterType type, Team team, int actor)
    {
        lock (_sync)
            return _counters.TryGetValue(type, out var teams) && teams.TryGetValue(team, out TeamCounter? counter)
                && counter.Actors.TryGetValue(actor, out int value) ? value : null;
    }

    public List<(Team Team, int Actor, int Value)> Ranking(GameStatCounterType type, bool higherIsBetter)
    {
        lock (_sync)
        {
            if (!_counters.TryGetValue(type, out var teams)) return [];
            var entries = teams.SelectMany(team => team.Value.Actors.Select(actor => (team.Key, actor.Key, actor.Value)));
            return [.. higherIsBetter ? entries.OrderByDescending(entry => entry.Value) : entries.OrderBy(entry => entry.Value)];
        }
    }

    public void RemoveActor(int actor)
    {
        lock (_sync)
            foreach (TeamCounter counter in _counters.Values.SelectMany(teams => teams.Values))
                counter.Actors.Remove(actor);
    }

    public void Clear()
    {
        lock (_sync) _counters.Clear();
    }

    public byte[] ToBytes()
    {
        lock (_sync)
        {
            var writer = new BytePackerWriter();
            writer.WriteInt32(_counters.Count);
            foreach ((GameStatCounterType type, var teams) in _counters)
            {
                writer.WriteByte((byte)type);
                writer.WriteInt32(teams.Count);
                foreach ((Team team, TeamCounter counter) in teams)
                {
                    writer.WriteByte((byte)team);
                    writer.WriteInt32(counter.TeamCount);
                    writer.WriteInt32(counter.Actors.Count);
                    foreach ((int actor, int count) in counter.Actors)
                    {
                        writer.WriteInt32(actor);
                        writer.WriteInt32(count);
                    }
                }
            }
            return writer.ToArray();
        }
    }
}
