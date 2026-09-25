using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class PublishPlanet(Session session) : IOperationHandler
{
    const short NotAuthorized = -2;
    const int PlanetImage = 0;

    public byte Code => (byte)OperationCode.PublishPlanet;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || session.WorldId is not int world
            || session.Play || session.OwnershipOf(player) != PlanetOwnership.Owner)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = NotAuthorized });
            return;
        }

        if (request.Parameters.TryGetValue((byte)ParameterKey.PlanetTextureData, out object? image) && image is byte[] { Length: > 0 } png
            && Stores.Images.Image(PlanetImage, world) is null)
            Stores.Images.SaveImage(PlanetImage, world, png);

        session.Publish();
        peer.Send(new OperationResponse(request));
    }
}
