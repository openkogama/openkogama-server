using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class UploadScreenshot(Session session) : IOperationHandler
{
    const int Planet = 0;

    public byte Code => (byte)OperationCode.UploadScreenshot;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int type = Convert.ToInt32(request[(byte)ParameterKey.ImageType]);
        int id = type == Planet && session.WorldId is int world ? world : Convert.ToInt32(request[(byte)ParameterKey.ImageID]);
        byte[] data = (byte[])request[(byte)ParameterKey.ImageData]!;

        Stores.Images.SaveImage(type, id, data);
        peer.Send(new OperationResponse(request));
        Console.WriteLine($"peer {peer.Id}: screenshot type {type} id {id} ({data.Length} bytes)");
    }
}
