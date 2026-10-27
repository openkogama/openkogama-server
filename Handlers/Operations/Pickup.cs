using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public static class Pickup
{
    const int RespawnMs = 3000;

    public static void Take(Session session, Player player, WorldObject pickup, int instigator)
    {
        AvatarItemType? item = pickup.Type == WorldObjectType.PickupCubeGun
            ? AvatarItemType.CubeGun
            : pickup.Data.Find(pair => pair.Key == "itemType").Value is int type ? (AvatarItemType)type : null;

        bool weapon = item is not (null or AvatarItemType.Health or AvatarItemType.Mutant or AvatarItemType.NinjaRun);
        if (weapon && Mutated(session, player)) return;

        Plugins.PluginHost.PickedUp(session, player, (int?)item);
        if (weapon && item is AvatarItemType held)
        {
            Equip(session, player, pickup, held, Holder(session, player, instigator));
            Plugins.PluginHost.SwitchedItem(session, player, (int)held);
        }

        Respawn(session, pickup.Id, player.Actor);
    }

    static bool Mutated(Session session, Player player) =>
        session.World.Find(player.PlayAvatar)?.Runtime.Find(pair => pair.Key == "modifiers").Value is List<(string Key, PackedType Type, object Value)> modifiers
        && modifiers.Exists(modifier => modifier.Key is "_Mutant" or "Mutant");

    static int Holder(Session session, Player player, int instigator) =>
        session.World.Find(instigator) is { } obj && obj.Owner == player.Actor && obj.Runtime.Exists(pair => pair.Key == "currentItem")
            ? instigator
            : player.PlayAvatar;

    public static void Respawn(Session session, int pickupId, int actor)
    {
        SendState(session, pickupId, actor, PickupItemState.Pickup);
        SendState(session, pickupId, actor, PickupItemState.Counting);

        _ = Task.Delay(RespawnMs).ContinueWith(_ =>
            SendState(session, pickupId, actor, PickupItemState.Listening));
    }

    static void Equip(Session session, Player player, WorldObject pickup, AvatarItemType weapon, int holderId)
    {
        int variant = pickup.Data.Find(pair => pair.Key == "variantId").Value as int? ?? 0;
        List<(string Key, PackedType Type, object Value)> currentItem =
        [
            ("type", PackedType.Int32, (int)weapon),
            ("variantId", PackedType.Int32, variant),
        ];
        var itemData = pickup.Data.Find(pair => pair.Key == "itemData");
        if (itemData.Key is not null) currentItem.Add(itemData);
        Hold(session, player, holderId, currentItem);
    }

    public static void Give(Session session, Player player, AvatarItemType item) =>
        Hold(session, player, player.PlayAvatar, [("type", PackedType.Int32, (int)item), ("variantId", PackedType.Int32, 0)]);

    public static void Give(Session session, Player player, AvatarItemType item, byte material) =>
        Hold(session, player, player.PlayAvatar,
        [
            ("type", PackedType.Int32, (int)item),
            ("variantId", PackedType.Int32, 0),
            ("itemData", PackedType.Hashtable, new List<(string, PackedType, object)> { ("material", PackedType.Byte, material) }),
        ]);

    static void Hold(Session session, Player player, int holderId, List<(string Key, PackedType Type, object Value)> currentItem)
    {
        session.World.Modify(holderId, holder => holder.SetRuntime("currentItem", PackedType.Hashtable, currentItem));

        var runtime = new Dictionary<object, object?> { ["currentItem"] = PackedData.ToPhoton(currentItem) };
        foreach (Player other in session.Players)
        {
            other.Peer.Send(new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = other == player ? 0 : player.Actor,
                    [(byte)ParameterKey.WorldObjectID] = holderId,
                    [(byte)ParameterKey.WorldObjectRunTimeData] = runtime,
                },
            });
        }
    }

    static void SendState(Session session, int pickupId, int actor, PickupItemState state)
    {
        var evt = new EventData((byte)EventCode.PickupItemStateChange)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = pickupId,
                [(byte)ParameterKey.ActorNr] = actor,
                [(byte)ParameterKey.PickupItemState] = (int)state,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
