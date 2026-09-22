using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateWorldObjectRunTimeData(PhotonServer server) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateWorldObjectRunTimeData;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        foreach (PhotonPeer other in server.Peers)
            if (other != peer)
                other.Send(evt);
    }
}
