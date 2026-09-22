using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

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
            info.Add((byte)ParameterKey.TeamID, 0);
            info.Add((byte)ParameterKey.Username, p.Username);
            info.Add((byte)ParameterKey.Level, 1);
            info.Add((byte)ParameterKey.RegionCode, p.Region);
            users.Add(p.Actor, info);
        }

        PhotonDictionary teams = PhotonDictionary.Untyped();
        for (int team = 0; team < 4; team++)
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)TeamDataKey.Active, team == 0);
            teams.Add(team, entry);
        }

        OperationResponse response = new(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.UserList] = users,
                [(byte)ParameterKey.TeamList] = teams,
                [(byte)ParameterKey.QueryId] = 1,
                [(byte)ParameterKey.GameStateType] = (int)GameStateType.Round,
                [(byte)ParameterKey.GameStateStartTime] = 0,
                [(byte)ParameterKey.GameStateDuration] = 0,
                [(byte)ParameterKey.GameStateReason] = 0,
                [(byte)ParameterKey.FineGrainedTerrainPrototypeID] = 101,
                [(byte)ParameterKey.GameStatCounterData] = new byte[sizeof(int)],
            },
        };

        peer.Send(response);
        Console.WriteLine($"peer {peer.Id}: game snapshot sent");
    }
}
