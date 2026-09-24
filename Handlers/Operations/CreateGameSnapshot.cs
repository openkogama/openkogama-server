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
        PhotonDictionary users = PhotonDictionary.Untyped();
        foreach (Player p in session.Players)
        {
            PhotonDictionary info = PhotonDictionary.ByteKeyed();
            info.Add((byte)ParameterKey.ProfileID, p.ProfileId);
            info.Add((byte)ParameterKey.TeamID, (int)p.Team);
            info.Add((byte)ParameterKey.Username, p.Username);
            info.Add((byte)ParameterKey.Level, p.Level);
            info.Add((byte)ParameterKey.RegionCode, p.Region);
            users.Add(p.Actor, info);
        }

        List<Team> active = session.Teams.Active;
        PhotonDictionary teams = PhotonDictionary.Untyped();
        foreach (Team team in Enum.GetValues<Team>())
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)TeamDataKey.Active, active.Contains(team));
            teams.Add((int)team, entry);
        }

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.UserList] = users,
                [(byte)ParameterKey.TeamList] = teams,
                [(byte)ParameterKey.QueryId] = 1,
                [(byte)ParameterKey.GameStateType] = (int)session.Round.State,
                [(byte)ParameterKey.GameStateStartTime] = session.Round.StartTime,
                [(byte)ParameterKey.GameStateDuration] = session.Round.Duration,
                [(byte)ParameterKey.GameStateReason] = (int)session.Round.Reason,
                [(byte)ParameterKey.FineGrainedTerrainPrototypeID] =
                    session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId ?? -1,
                [(byte)ParameterKey.GameStatCounterData] = session.Round.Stats.ToBytes(),
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: game snapshot sent");
    }
}
