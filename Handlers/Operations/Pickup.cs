using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public static class Pickup
{
    const int RespawnMs = 3000;

    public static void Take(Session session, Player player, WorldObject pickup)
    {
        AvatarItemType? item = pickup.Type == WorldObjectType.PickupCubeGun
            ? AvatarItemType.CubeGun
            : pickup.Data.Find(pair => pair.Key == "itemType").Value is int type ? (AvatarItemType)type : null;

        if (item is AvatarItemType weapon && weapon is not (AvatarItemType.Health or AvatarItemType.Mutant or AvatarItemType.NinjaRun))
            Equip(session, player, pickup, weapon);

        Respawn(session, pickup.Id, player.Actor);
    }

    public static void Respawn(Session session, int pickupId, int actor)
    {
        SendState(session, pickupId, actor, PickupItemState.Pickup);
        SendState(session, pickupId, actor, PickupItemState.Counting);

        _ = Task.Delay(RespawnMs).ContinueWith(_ =>
            SendState(session, pickupId, actor, PickupItemState.Listening));
    }

    static void Equip(Session session, Player player, WorldObject pickup, AvatarItemType weapon)
    {
        int variant = pickup.Data.Find(pair => pair.Key == "variantId").Value as int? ?? 0;
        List<(string Key, PackedType Type, object Value)> currentItem =
        [
            ("type", PackedType.Int32, (int)weapon),
            ("variantId", PackedType.Int32, variant),
        ];
        var itemData = pickup.Data.Find(pair => pair.Key == "itemData");
        if (itemData.Key is not null) currentItem.Add(itemData);

        int avatarId = player.PlayAvatar;
        session.World.Modify(avatarId, avatar => avatar.SetRuntime("currentItem", PackedType.Hashtable, currentItem));

        var runtime = new Dictionary<object, object?> { ["currentItem"] = PackedData.ToPhoton(currentItem) };
        foreach (Player other in session.Players)
        {
            other.Peer.Send(new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = other == player ? 0 : player.Actor,
                    [(byte)ParameterKey.WorldObjectID] = avatarId,
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
