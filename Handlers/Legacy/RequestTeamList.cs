using OpenKogama.Game;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public sealed class RequestTeamList : ILegacyHandler
{
    public string Operation => "RequestTeamList";

    public void Handle(PhotonPeer peer, OperationRequest request, LegacyTranslator protocol, Session session)
    {
        List<Team> active = session.Teams.Active;
        var teams = new Dictionary<object, object?>();
        foreach (Team team in Enum.GetValues<Team>())
        {
            teams[(int)team] = new Dictionary<object, object?>
            {
                [(byte)0] = active.Contains(team),
                [(byte)1] = 0,
            };
        }

        protocol.SendRaw(peer, new OperationResponse(request)
        {
            Parameters = { [protocol.Key("TeamList")] = teams },
        });
    }
}
