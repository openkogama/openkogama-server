using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class InitializeAvatarEdit(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.InitializeAvatarEdit;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var writer = new BytePackerWriter();
        writer.WriteInt32(session.Bodies.Count);
        foreach (int body in session.Bodies)
        {
            writer.WriteInt32(body);
            AvatarMetaData.Write(writer, body);
        }

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.AvatarMetaDataWoMap] = writer.ToArray() },
        });
    }
}
