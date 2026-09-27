using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public enum Team { Blue, Red, Green, Yellow }

public sealed class Teams
{
    static readonly Dictionary<WorldObjectType, Team> Spawns = new()
    {
        [WorldObjectType.SpawnPointBlue] = Team.Blue,
        [WorldObjectType.SpawnPointRed] = Team.Red,
        [WorldObjectType.SpawnPointGreen] = Team.Green,
        [WorldObjectType.SpawnPointYellow] = Team.Yellow,
    };

    readonly Session _session;
    readonly object _sync = new();
    List<Team> _active;

    public Teams(Session session)
    {
        _session = session;
        _active = FromWorld();
    }

    public List<Team> Active
    {
        get { lock (_sync) return [.. _active]; }
    }

    public Team Default => Active[0];

    public bool Set(Player player, Team team)
    {
        if (!Active.Contains(team)) return false;

        player.Team = team;
        _session.Round.Stats.RemoveActor(player.Actor);
        var evt = new EventData((byte)EventCode.SetTeam)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.TeamID] = (int)team,
            },
        };
        foreach (Player other in _session.Players)
            other.Peer.Send(evt);
        return true;
    }

    public void Update()
    {
        List<Team> added, removed;
        lock (_sync)
        {
            List<Team> now = FromWorld();
            added = [.. now.Except(_active)];
            removed = [.. _active.Except(now)];
            _active = now;
        }

        foreach (Team team in added) Broadcast(EventCode.AddTeam, team);

        List<Player> moved = [.. _session.Players.Where(player => removed.Contains(player.Team))];
        foreach (Team team in removed)
        {
            PhotonDictionary actors = PhotonDictionary.Untyped();
            foreach (Player player in moved.Where(player => player.Team == team))
                actors.Add(player.Actor, (int)Default);
            Broadcast(EventCode.RemoveTeam, team, actors);
        }

        foreach (Player player in moved)
            Set(player, Default);
    }

    List<Team> FromWorld()
    {
        List<Team> teams = [.. _session.World.ToSnapshot().Objects
            .Where(obj => Spawns.ContainsKey(obj.Type))
            .Select(obj => Spawns[obj.Type])
            .Distinct()
            .Order()];
        return teams.Count > 0 ? teams : [Team.Blue];
    }

    void Broadcast(EventCode code, Team team, PhotonDictionary? actors = null)
    {
        var evt = new EventData((byte)code)
        {
            Parameters = { [(byte)ParameterKey.TeamID] = (int)team },
        };
        if (actors is not null) evt.Parameters[(byte)ParameterKey.Data] = actors;
        foreach (Player player in _session.Players)
            player.Peer.Send(evt);
    }
}
