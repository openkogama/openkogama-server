using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class CreateGameSnapshot(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.CreateGameSnapshot;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? me = session.For(peer);

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.UserList] = Users(session, me),
                [(byte)ParameterKey.TeamList] = Teams(session),
                [(byte)ParameterKey.QueryId] = 1,
                [(byte)ParameterKey.GameStateType] = (int)session.Round.State,
                [(byte)ParameterKey.GameStateStartTime] = session.Round.StartTime,
                [(byte)ParameterKey.GameStateDuration] = me is null ? session.Round.Duration : session.Round.DurationFor(me),
                [(byte)ParameterKey.GameStateReason] = (int)session.Round.Reason,
                [(byte)ParameterKey.FineGrainedTerrainPrototypeID] =
                    session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId ?? -1,
                [(byte)ParameterKey.GameStatCounterData] = session.Round.Stats.ToBytes(),
                [(byte)ParameterKey.FrameCount] = session.Logic.Frame,
                [(byte)ParameterKey.Timestamp] = me?.LogicSteps == true ? session.Logic.StepStamp : session.Logic.Frame * Logic.FrameInterval,
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: game snapshot sent");
    }

    public static EventData Setup(Session session, Player me)
    {
        EventCode code = me.Mode switch
        {
            GameMode.CharacterEditor => EventCode.SetupUserAvatarEdit,
            GameMode.Edit => EventCode.SetupUserBuildMode,
            _ => EventCode.SetupUserPlayMode,
        };
        var setup = new EventData((byte)code)
        {
            Parameters =
            {
                [(byte)ParameterKey.Data] = me.SpawnRoleData(),
                [(byte)ParameterKey.MetaData] = me.SpawnRoleMetaData(),
                [(byte)ParameterKey.Id] = me.Mode == GameMode.CharacterEditor || me.AvatarId < 0 ? session.Bodies.FirstOrDefault(-1) : session.BodyOf(me.AvatarId),
                [(byte)ParameterKey.Timestamp] = me.LogicSteps ? session.Logic.StepStamp : session.Logic.Frame * Logic.FrameInterval,
                [(byte)ParameterKey.TeamList] = Teams(session),
            },
        };
        if (code == EventCode.SetupUserAvatarEdit) return setup;

        setup.Parameters[(byte)ParameterKey.UserList] = Users(session, me);
        setup.Parameters[(byte)ParameterKey.GameStateType] = (int)session.Round.State;
        setup.Parameters[(byte)ParameterKey.GameStateStartTime] = session.Round.StartTime;
        setup.Parameters[(byte)ParameterKey.GameStateDuration] = session.Round.DurationFor(me);
        setup.Parameters[(byte)ParameterKey.GameStatCounterData] = session.Round.Stats.ToBytes();
        return setup;
    }

    static PhotonDictionary Users(Session session, Player? me)
    {
        PhotonDictionary users = PhotonDictionary.Untyped();
        foreach (Player p in session.Players)
        {
            PhotonDictionary info = PhotonDictionary.ByteKeyed();
            foreach ((ParameterKey key, object value) in p.Info()) info.Add((byte)key, value);
            if (me?.SpawnRoles == true) info.Add((byte)ParameterKey.Data, p.SpawnRoleData());
            users.Add(p.Actor, info);
            me?.Sees(p.Actor);
        }
        return users;
    }

    static PhotonDictionary Teams(Session session)
    {
        List<Team> active = session.Teams.Active;
        PhotonDictionary teams = PhotonDictionary.Untyped();
        foreach (Team team in Enum.GetValues<Team>())
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)TeamDataKey.Active, active.Contains(team));
            teams.Add((int)team, entry);
        }
        return teams;
    }
}
