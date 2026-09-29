using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class GamePasses
{
    const string Enabled = "gamePassProgressionEnabled";
    const string Rewards = "gamePassProgressionDataObject";
    const string Validation = "gamePassProgressionDataObjectValidation";
    const string DefaultRewards = """{"xpTierRewards":{"xpTierRewards":{"Tier1":100,"Tier2":200,"Tier3":400}}}""";
    const string RewardRanges = """{"XpTiersRewardsValidator":{"xpTierRewardsValidators":{"Tier1":{"isRemovalAllowed":false,"rangeValidator":{"min":25,"max":500}},"Tier2":{"isRemovalAllowed":false,"rangeValidator":{"min":100,"max":1000}},"Tier3":{"isRemovalAllowed":false,"rangeValidator":{"min":200,"max":3000}}}}}""";
    const int CheckInterval = 1000;

    readonly Session _session;
    readonly int _id = -1;
    bool _enabled;
    long _next;

    public GamePasses(Session session, bool enabled)
    {
        _session = session;
        if (!enabled) return;

        GameWorld world = session.World;
        _enabled = HasCrystals(world);
        if (world.FindFirst(WorldObjectType.GamePassProgressionDataObject) is { } existing)
        {
            _id = existing.Id;
            world.Modify(_id, obj => Set(obj, _enabled));
            return;
        }

        _id = world.NewObjectId();
        world.Add(new WorldObject
        {
            Id = _id,
            ParentId = world.RootId,
            Type = WorldObjectType.GamePassProgressionDataObject,
            Data = [(Rewards, PackedType.String, DefaultRewards), (Enabled, PackedType.Bool, _enabled)],
            Runtime = [(Validation, PackedType.String, RewardRanges)],
        });
    }

    public void Tick()
    {
        if (_id < 0 || Environment.TickCount64 < _next) return;
        _next = Environment.TickCount64 + CheckInterval;

        bool enabled = HasCrystals(_session.World);
        if (enabled == _enabled || _session.World.Find(_id) is null) return;
        _enabled = enabled;
        _session.World.Modify(_id, obj => Set(obj, enabled));

        var update = new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = _id,
                [(byte)ParameterKey.WorldObjectData] = new Dictionary<object, object?> { [Enabled] = enabled },
            },
        };
        Snapshot progression = _session.World.SubtreeSnapshot(_id);
        foreach (Player player in _session.Players)
            if (player.InWorld && player.Knows(progression))
                player.Peer.Send(update);
    }

    static bool HasCrystals(GameWorld world) =>
        world.FindFirst(WorldObjectType.GamePoint) is not null || world.FindFirst(WorldObjectType.GamePointChest) is not null;

    static void Set(WorldObject obj, bool enabled)
    {
        obj.Data.RemoveAll(pair => pair.Key == Enabled);
        obj.Data.Add((Enabled, PackedType.Bool, enabled));
        if (!obj.Data.Exists(pair => pair.Key == Rewards)) obj.Data.Add((Rewards, PackedType.String, DefaultRewards));
        obj.SetRuntime(Validation, PackedType.String, RewardRanges);
    }
}
