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
        EventData? withHand = null;
        if (PhotonValues.Normalize(request[(byte)ParameterKey.WorldObjectRunTimeData]) is Dictionary<object, object?> changes)
        {
            if (changes.GetValueOrDefault("currentItem") is Dictionary<object, object?> item
                && item.GetValueOrDefault("type") is { } type && Convert.ToInt32(type) == (int)AvatarItemType.LaserPointer
                && session.World.Find(objectId) is { Type: WorldObjectType.Avatar })
            {
                withHand = new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
                {
                    Parameters = new Dictionary<byte, object?>(request.Parameters)
                    {
                        [(byte)ParameterKey.WorldObjectRunTimeData] = new Dictionary<object, object?>(changes)
                        {
                            ["currentItem"] = new Dictionary<object, object?> { ["type"] = (int)AvatarItemType.Hand },
                        },
                        [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                    },
                };
            }

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

        bool builder = session.Players.Any(player => player.BuildAvatarId == objectId);
        foreach (Player player in session.Players)
            if (player.Peer != peer && (!builder || player.SpawnRoles))
                player.Peer.Send(withHand is not null && player.SpawnRoles ? withHand : evt);
    }
}
