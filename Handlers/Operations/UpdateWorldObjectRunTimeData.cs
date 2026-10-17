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
        if (Teleports.IsPrivate(objectId)) return;
        EventData? withHand = null;
        EventData? limited = null;
        if (PhotonValues.Normalize(request[(byte)ParameterKey.WorldObjectRunTimeData]) is Dictionary<object, object?> changes)
        {
            if (changes.GetValueOrDefault("animation") is { } animation && session.For(peer) is Player mover && mover.Owns(objectId)
                && (animation is Dictionary<object, object?> table ? table.GetValueOrDefault("state") : animation) is string state)
                Plugins.PluginHost.Animated(session, mover, state);
            if (changes.GetValueOrDefault("currentItem") is Dictionary<object, object?> held && held.GetValueOrDefault("type") is { } heldType
                && session.For(peer) is Player switcher && switcher.Owns(objectId))
                Plugins.PluginHost.SwitchedItem(session, switcher, Convert.ToInt32(heldType));
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

            if (AvatarModifiers.Without(changes) is { } known)
            {
                limited = new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
                {
                    Parameters = new Dictionary<byte, object?>(request.Parameters)
                    {
                        [(byte)ParameterKey.WorldObjectRunTimeData] = known,
                        [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                    },
                };
            }

            try
            {
                session.World.Modify(objectId, obj =>
                {
                    if (changes.GetValueOrDefault("currentItem") is Dictionary<object, object?>)
                        obj.Runtime.RemoveAll(pair => pair.Key == "currentItem");
                    PackedData.Merge(obj.Runtime, changes);
                });
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

        bool builder = session.Players.Any(player => player.ExtraRole(objectId));
        foreach (Player player in session.Players)
            if (player.Peer != peer && (!builder || player.SpawnRoles))
                player.Peer.Send(withHand is not null && player.SpawnRoles ? withHand
                    : limited is not null && AvatarModifiers.Limited(player) ? limited : evt);
    }
}
