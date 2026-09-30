using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
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
    const int MaxTier = 3;
    const int SecondsPerXp = 5;
    const int WelcomeReward = 8;
    const byte TierUnlockedReward = 7;
    const short InsufficientFunds = 1;
    const short AlreadyPurchased = 2;
    const short Failed = 4;
    static readonly int[] FallbackXp = [0, 100, 200, 400];

    readonly Session _session;
    readonly int _id = -1;
    readonly Dictionary<int, int> _testTiers = [];
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

    public void Send(Player player)
    {
        if (_id < 0 || !Supports(player, EventCode.GetPublishedPlanetProfileData)) return;

        string package = JsonSerializer.Serialize(new { playerPlanetData = PlanetData(player), playerTierStateCalculator = Calculator() });
        player.Peer.Send(new EventData((byte)EventCode.GetPublishedPlanetProfileData) { Parameters = { [(byte)ParameterKey.Data] = package } });
        Console.WriteLine($"peer {player.Peer.Id}: game tiers sent");
    }

    public short Purchase(Player player, int tier)
    {
        if (_id < 0 || !_enabled || !_session.Play || _session.WorldId is not int world) return Failed;

        (int current, int seen) = Stores.Profiles.GameTier(player.ProfileId, world);
        if (tier <= current) return AlreadyPurchased;
        if (tier != current + 1 || tier > MaxTier) return Failed;

        int xp = TierXp()[tier];
        int price = xp / 2;
        if (Stores.Profiles.Gold(player.ProfileId) < price) return InsufficientFunds;

        Stores.Profiles.AddGold(player.ProfileId, -price);
        Stores.Profiles.SetGameTier(player.ProfileId, world, tier, seen);
        Experience.Award(player, xp, TierUnlockedReward);
        Update(player);
        Console.WriteLine($"profile {player.ProfileId}: bought tier {tier} in world {world} for {price} gold");
        return 0;
    }

    public void Test(Player player, int tier)
    {
        if (_id < 0 || _session.Play) return;
        _testTiers[player.Actor] = Math.Clamp(tier, 0, MaxTier);
        Update(player);
    }

    public void Seen(Player player, int tier)
    {
        if (!_session.Play || _session.WorldId is not int world) return;
        (int current, _) = Stores.Profiles.GameTier(player.ProfileId, world);
        Stores.Profiles.SetGameTier(player.ProfileId, world, current, Math.Clamp(tier, 0, current));
    }

    public void Reset(Player player)
    {
        if (_id < 0) return;
        if (_session.Play && _session.WorldId is int world) Stores.Profiles.SetGameTier(player.ProfileId, world, 0, 0);
        else _testTiers.Remove(player.Actor);
        Update(player);
    }

    void Update(Player player)
    {
        if (!Supports(player, EventCode.PlayerPlanetData)) return;
        player.Peer.Send(new EventData((byte)EventCode.PlayerPlanetData)
        {
            Parameters = { [(byte)ParameterKey.Data] = JsonSerializer.Serialize(PlanetData(player)) },
        });
    }

    object PlanetData(Player player)
    {
        (int tier, int seen) = _session.Play && _session.WorldId is int world
            ? Stores.Profiles.GameTier(player.ProfileId, world)
            : (_testTiers.GetValueOrDefault(player.Actor), _testTiers.GetValueOrDefault(player.Actor));
        return new
        {
            highScoreGamePoints = 0,
            rank = 0,
            progressionGamePoints = 0,
            playtime = "00:00:00",
            gamePassTier = tier,
            playerPlanetMetaData = new { gamePassTierSeen = seen, welcomeRewardClaimed = true, lastDailyWelcomeRewardClaim = "0001-01-01T00:00:00" },
            previewGamePassTier = 0,
        };
    }

    object Calculator()
    {
        int[] xp = TierXp();
        var thresholds = new Dictionary<string, object>();
        for (int tier = 0; tier <= MaxTier; tier++)
            thresholds[$"Tier{tier}"] = new
            {
                goldPriceRequirement = xp[tier] / 2,
                gamePointRequirement = xp[tier] * 5 / 6,
                estimatedRequiredPlaytime = TimeSpan.FromSeconds(xp[tier] * SecondsPerXp).ToString(@"hh\:mm\:ss"),
            };
        return new { gamePassRewardsActivated = true, gamePointVelocityIsZero = false, welcomeReward = WelcomeReward, progressionThresholds = thresholds };
    }

    int[] TierXp()
    {
        int[] xp = [.. FallbackXp];
        if (_session.World.Find(_id)?.Data.Find(pair => pair.Key == Rewards).Value is not string json) return xp;
        try
        {
            JsonElement rewards = JsonDocument.Parse(json).RootElement.GetProperty("xpTierRewards").GetProperty("xpTierRewards");
            for (int tier = 1; tier <= MaxTier; tier++)
                if (rewards.TryGetProperty($"Tier{tier}", out JsonElement value) || rewards.TryGetProperty(tier.ToString(), out value))
                    xp[tier] = value.GetInt32();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
        return xp;
    }

    static bool Supports(Player player, EventCode code) =>
        player.Peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(code);

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
