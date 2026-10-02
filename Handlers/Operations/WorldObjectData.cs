using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public enum DataChange { Replace, Merge, Remove }

public sealed class WorldObjectData(Session session, DataChange change) : IOperationHandler
{
    public byte Code => (byte)(change switch
    {
        DataChange.Replace => OperationCode.UpdateWorldObjectData,
        DataChange.Merge => OperationCode.UpdateWorldObjectDataPartial,
        _ => OperationCode.RemoveWorldObjectDataPartial,
    });

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        byte dataKey = (byte)(change == DataChange.Remove ? ParameterKey.WorldObjectDataToRemove : ParameterKey.WorldObjectData);
        object? raw = request[dataKey];
        var table = (Dictionary<object, object?>)PhotonValues.Normalize(raw)!;

        bool found = session.World.Modify(objectId, obj =>
        {
            switch (change)
            {
                case DataChange.Replace: obj.Data = PackedData.FromPhoton(table); break;
                case DataChange.Merge: PackedData.Merge(obj.Data, table); break;
                case DataChange.Remove: PackedData.Remove(obj.Data, table); break;
            }
        });

        if (!found)
        {
            Console.WriteLine($"peer {peer.Id}: data change on unknown object {objectId}");
            return;
        }

        session.World.MarkChanged();
        if (session.World.Find(objectId)?.Type == WorldObjectType.AvatarSpawnRoleCreator)
        {
            AvatarClasses.SyncSkills(session.World, objectId);
            session.Teams.Update();
        }

        var evt = new EventData((byte)(change switch
        {
            DataChange.Replace => EventCode.UpdateWorldObjectData,
            DataChange.Merge => EventCode.UpdateWorldObjectDataPartial,
            _ => EventCode.RemoveWorldObjectDataPartial,
        }))
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [dataKey] = raw,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
