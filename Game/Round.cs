using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Round(Session session)
{
    const int EndedMs = 5000;
    const int PrepareMs = 5000;

    readonly HashSet<(int Collectible, int Actor)> _collected = [];
    readonly Dictionary<int, (float Health, int DiedAt)> _ghosts = [];
    readonly object _sync = new();
    int _version;

    public GameStateType State { get; private set; } = GameStateType.Round;
    public GameStateReason Reason { get; private set; }
    public int StartTime { get; private set; }
    public int Duration { get; private set; } = -1;
    public GameStats Stats { get; } = new();

    public void Collect(Player player, WorldObject collectible)
    {
        lock (_sync)
            if (State != GameStateType.Round || !_collected.Add((collectible.Id, player.Actor))) return;

        Broadcast(EventCode.CollectiblePickedUp, new()
        {
            [(byte)ParameterKey.WorldObjectID] = collectible.Id,
            [(byte)ParameterKey.ActorNr] = player.Actor,
        });

        int count = Score(player, GameStatCounterType.Collectible);
        int total = session.World.ToSnapshot().Objects.Count(obj => obj.Type == WorldObjectType.CollectibleItem);
        if (count >= total) End(GameStateReason.AllCollectiblesFound, player.Actor);
    }

    public void CaptureFlag(Player player)
    {
        if (State != GameStateType.Round || session.World.FindFirst(WorldObjectType.Flag) is null) return;

        int elapsed = Math.Max(session.Clock() - StartTime, 0);
        Score(player, GameStatCounterType.Flag, elapsed);
        End(GameStateReason.FlagReached, player.Actor);
    }

    public void ReportKill(Player killer, Player victim)
    {
        if (State != GameStateType.Round || killer == victim) return;
        if (session.Teams.Active.Count > 1 && killer.Team == victim.Team) return;

        int count = Score(killer, GameStatCounterType.Kill);
        if (count >= Limit(WorldObjectType.KillLimit)) End(GameStateReason.None, killer.Actor);
    }

    public void GhostHit(Player shooter, WorldObject ghost, float damage)
    {
        const float GhostHealth = 80f;
        const int GhostRespawnMs = 15000;

        if (State != GameStateType.Round) return;

        bool killed;
        lock (_sync)
        {
            (float health, int diedAt) = _ghosts.GetValueOrDefault(ghost.Id, (GhostHealth, int.MinValue / 2));
            if (session.Clock() - diedAt < GhostRespawnMs) return;

            health -= damage;
            killed = health < 0;
            _ghosts[ghost.Id] = killed ? (GhostHealth, session.Clock()) : (health, diedAt);
        }
        if (!killed) return;

        int count = Score(shooter, GameStatCounterType.OculusKill);
        if (count >= Limit(WorldObjectType.OculusKillLimit)) End(GameStateReason.None, shooter.Actor);
    }

    int Limit(WorldObjectType type) =>
        session.World.FindFirst(type)?.Data.Find(pair => pair.Key == "killLimit").Value as int? ?? int.MaxValue;

    int Score(Player player, GameStatCounterType type, int value = 1)
    {
        int count = Stats.Increment(type, player.Team, player.Actor, value, includeTeamScore: true);
        SendStat(player.Actor, player.Team, type, value, increment: true);
        return count;
    }

    void SendStat(int actor, Team team, GameStatCounterType type, int value, bool increment) =>
        Broadcast(EventCode.UpdateGameStat, new()
        {
            [(byte)ParameterKey.ActorNr] = actor,
            [(byte)ParameterKey.TeamID] = (int)team,
            [(byte)ParameterKey.StatCounterType] = (byte)type,
            [(byte)ParameterKey.GameStatValue] = value,
            [(byte)ParameterKey.GameStatOtherID] = -1,
            [(byte)ParameterKey.GameStatUpdateTeamScore] = true,
            [(byte)ParameterKey.GameStatIncrement] = increment,
        });

    public void Start()
    {
        lock (_sync)
        {
            _collected.Clear();
            _ghosts.Clear();
        }
        Stats.Clear();
        session.World.ClearRuntimeEvents();

        if (session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId is int id)
            session.World.FindPrototype(id)?.Cubes.Clear();
        Broadcast(EventCode.ResetTerrain, []);

        int duration = RoundCube() is WorldObject cube ? (cube.Data.Find(pair => pair.Key == "interval").Value as int? ?? 0) * 1000 : -1;
        SetState(GameStateType.Round, duration > 0 ? duration : -1, GameStateReason.None, 0);
        if (duration > 0) After(duration, _version, TimeUp);
    }

    public void TrackHeight(Player player, float y)
    {
        if (State != GameStateType.Round || RoundCube() is not WorldObject cube) return;

        var condition = (GameStatCounterType)(cube.Data.Find(pair => pair.Key == "winningCondition").Value as int? ?? 0);
        if (condition is not (GameStatCounterType.YUp or GameStatCounterType.YDown)) return;

        bool up = condition == GameStatCounterType.YUp;
        int height = (int)MathF.Floor(y);
        int? best = Stats.Get(condition, player.Team, player.Actor);
        if (best is int record && (up ? height <= record : height >= record)) return;

        Stats.Set(condition, player.Team, player.Actor, height, higherIsBetter: up);
        SendStat(player.Actor, player.Team, condition, height, increment: false);
    }

    void TimeUp()
    {
        const Team ServerTeam = (Team)4;
        SendStat(0, ServerTeam, GameStatCounterType.Time, 0, increment: false);
        End(GameStateReason.Timeout, 0);
    }

    WorldObject? RoundCube() => session.World.FindFirst(WorldObjectType.RoundCube);

    void End(GameStateReason reason, int actor)
    {
        ReportWinner(reason, actor);
        SetState(GameStateType.RoundEnded, EndedMs, reason, actor);

        int version = _version;
        After(EndedMs, version, () =>
        {
            SetState(GameStateType.PrepareRound, PrepareMs, GameStateReason.None, 0);
            After(PrepareMs, _version, Start);
        });
    }

    void SetState(GameStateType state, int duration, GameStateReason reason, int actor)
    {
        lock (_sync)
        {
            State = state;
            Reason = reason;
            StartTime = session.Clock();
            Duration = duration;
            _version++;
        }

        Broadcast(EventCode.GameStateChange, new()
        {
            [(byte)ParameterKey.ActorNr] = actor,
            [(byte)ParameterKey.GameStateType] = (int)State,
            [(byte)ParameterKey.GameStateReason] = (int)Reason,
            [(byte)ParameterKey.GameStateStartTime] = StartTime,
            [(byte)ParameterKey.GameStateDuration] = Duration,
        });
    }

    void After(int milliseconds, int version, Action action) =>
        _ = Task.Delay(milliseconds).ContinueWith(_ =>
        {
            if (_version == version) action();
        });

    void ReportWinner(GameStateReason reason, int actor)
    {
        GameStatCounterType condition = reason switch
        {
            GameStateReason.FlagReached => GameStatCounterType.Flag,
            GameStateReason.AllCollectiblesFound => GameStatCounterType.Collectible,
            GameStateReason.Timeout => (GameStatCounterType)(RoundCube()?.Data.Find(pair => pair.Key == "winningCondition").Value as int? ?? 0),
            _ => GameStatCounterType.Kill,
        };
        bool higherIsBetter = condition is not (GameStatCounterType.Flag or GameStatCounterType.YDown);

        List<(Team Team, int Actor, int Value)> ranking = Stats.Ranking(condition, higherIsBetter);
        var winner = ranking.FirstOrDefault(entry => entry.Actor == actor);
        if (winner.Actor == 0 && ranking.Count > 0) winner = ranking[0];

        var report = new WinnerReport(condition, winner.Actor == 0 ? null : (winner.Actor, winner.Team, winner.Value), session.Teams.Active.Count > 1);
        var evt = new EventData((byte)EventCode.PostWinnerReport);
        foreach (Player player in session.Players)
        {
            if (player.Peer.Translator is Kogama.Protocols.LegacyTranslator legacy)
                legacy.SendRaw(player.Peer, Handlers.Legacy.LegacyEvents.WinnerReport(legacy, report));
            else
                player.Peer.Send(evt);
        }
    }

    void Broadcast(EventCode code, Dictionary<byte, object?> parameters)
    {
        var evt = new EventData((byte)code) { Parameters = parameters };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
