using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetTeam : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetTeam;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int team = Convert.ToInt32(request[(byte)ParameterKey.TeamID]);

        peer.Send(new OperationResponse(request));

        peer.Send(new EventData((byte)EventCode.SetTeam)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.TeamID] = team,
            },
        });

        Console.WriteLine($"peer {peer.Id}: team {team}");
    }
}
