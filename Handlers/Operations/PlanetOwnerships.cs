using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetPlanetOwnerships(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetPlanetOwnerships;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var entries = session.Players.Select(player => new { profileID = player.ProfileId, planetOwnership = (int)session.OwnershipOf(player) }).ToList();
        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.PlanetOwnershipType] = JsonSerializer.Serialize(new { planetOwnerships = entries }) },
        });
    }
}

public sealed class GetAllPlanetPermissions(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetAllPlanetPermissions;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var permissions = session.Players
            .GroupBy(player => player.ProfileId)
            .ToDictionary(group => group.Key.ToString(), group => session.OwnershipOf(group.First()) == PlanetOwnership.None ? Array.Empty<int>() : Join.AllPermissions);
        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.PlanetPermissions] = JsonSerializer.Serialize(permissions) },
        });
    }
}
