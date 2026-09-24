using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetTeam(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetTeam;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var team = (Team)Convert.ToInt32(request[(byte)ParameterKey.TeamID]);
        Player? player = session.For(peer);
        if (player is null) return;

        peer.Send(new OperationResponse(request));

        if (!session.Teams.Set(player, team))
            session.Teams.Set(player, session.Teams.Default);

        Console.WriteLine($"peer {peer.Id}: team {player.Team}");
    }
}
