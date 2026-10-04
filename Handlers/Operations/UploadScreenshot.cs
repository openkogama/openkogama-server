using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class UploadScreenshot(Session session) : IOperationHandler
{
    const int Planet = 0;
    const int Avatar = 1;

    public byte Code => (byte)OperationCode.UploadScreenshot;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int type = Convert.ToInt32(request[(byte)ParameterKey.ImageType]);
        int id = type switch
        {
            Planet when session.WorldId is int world => world,
            Avatar when session.For(peer) is Player player => Stores.Profiles.ActiveAvatar(player.ProfileId),
            _ => Convert.ToInt32(request[(byte)ParameterKey.ImageID]),
        };
        if ((request[(byte)ParameterKey.ImageData] as byte[] ?? session.For(peer)?.TakeUpload()) is not byte[] data)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Stores.Images.SaveImage(type, id, data);
        peer.Send(new OperationResponse(request));
        Console.WriteLine($"peer {peer.Id}: screenshot type {type} id {id} ({data.Length} bytes)");
    }
}
