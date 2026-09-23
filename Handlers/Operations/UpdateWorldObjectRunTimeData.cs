using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateWorldObjectRunTimeData(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateWorldObjectRunTimeData;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (PhotonValues.Normalize(request[(byte)ParameterKey.WorldObjectRunTimeData]) is Dictionary<object, object?> changes)
        {
            try
            {
                session.World.Modify(objectId, obj => PackedData.Merge(obj.Runtime, changes));
            }
            catch (NotSupportedException error)
            {
                Console.WriteLine($"peer {peer.Id}: runtime of {objectId} not stored, {error.Message}");
            }
        }

        var evt = new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        foreach (Player player in session.Players)
            if (player.Peer != peer)
                player.Peer.Send(evt);
    }
}
