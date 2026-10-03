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
    const string ChestAmount = "gamePointAmount";
    const int ListSize = 13;
    const int ListMiddle = 6;
    const short InsufficientFunds = 1;
    const short AlreadyPurchased = 2;
    const short Failed = 4;
    static readonly int[] FallbackXp = [0, 100, 200, 400];

    readonly Session _session;
    readonly int _id = -1;
    readonly Dictionary<int, HashSet<int>> _collected = [];
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
        if (_id < 0 || !_enabled || !_session.Play || Key is not int key) return Failed;

        PlanetProgress progress = Stores.Profiles.Planet(player.ProfileId, key);
        int current = Math.Max(progress.Tier, EarnedTier(progress.Points));
        if (tier <= current) return AlreadyPurchased;
        if (tier != current + 1 || tier > MaxTier) return Failed;

        int price = RemainingPrice(progress.Points, tier);
        if (Stores.Profiles.Gold(player.ProfileId) < price) return InsufficientFunds;

        Stores.Profiles.AddGold(player.ProfileId, -price);
        Stores.Profiles.SetPlanet(player.ProfileId, key, progress with { Tier = tier });
        Experience.Award(player, TierXp()[tier], TierUnlockedReward);
        Update(player);
        Console.WriteLine($"profile {player.ProfileId}: bought tier {tier} in world {key} for {price} gold");
        return 0;
    }

    public void Test(Player player, int tier)
    {
        if (_id < 0 || _session.Play || Key is not int key) return;
        PlanetProgress progress = Stores.Profiles.Planet(player.ProfileId, key);
        int chosen = Math.Clamp(tier, 0, MaxTier);
        Stores.Profiles.SetPlanet(player.ProfileId, key, progress with { Tier = chosen, Seen = chosen, Points = 0 });
        Update(player);
    }

    public void Seen(Player player, int tier)
    {
        if (Key is not int key) return;
        PlanetProgress progress = Stores.Profiles.Planet(player.ProfileId, key);
        int current = Math.Max(progress.Tier, EarnedTier(progress.Points));
        Stores.Profiles.SetPlanet(player.ProfileId, key, progress with { Seen = Math.Clamp(tier, 0, current) });
    }

    public void Reset(Player player)
    {
        if (_id < 0 || Key is not int key) return;
        PlanetProgress progress = Stores.Profiles.Planet(player.ProfileId, key);
        Stores.Profiles.SetPlanet(player.ProfileId, key, progress with { Tier = 0, Seen = 0, Points = 0 });
        _collected.Remove(player.Actor);
        Update(player);
    }

    public void ClaimWelcome(Player player, bool doubled)
    {
        if (_id < 0 || !_enabled || Key is not int key) return;
        PlanetProgress progress = Stores.Profiles.Planet(player.ProfileId, key);
        if (progress.Welcome?.Date == DateTime.UtcNow.Date) return;
        AddPoints(player, key, progress with { Welcome = DateTime.UtcNow }, WelcomeReward * (doubled ? 2 : 1));
    }

    public void Collect(Player player, WorldObject crystal)
    {
        if (_id < 0 || !_enabled || Key is not int key) return;
        HashSet<int> collected = _collected.TryGetValue(player.Actor, out HashSet<int>? set) ? set : _collected[player.Actor] = [];
        if (!collected.Add(crystal.Id)) return;

        int amount = crystal.Type == WorldObjectType.GamePoint
            ? 1
            : crystal.Data.Find(pair => pair.Key == ChestAmount).Value is int value ? value : 0;
        if (amount > 0) AddPoints(player, key, Stores.Profiles.Planet(player.ProfileId, key), amount);
    }

    public void ResetCrystals() => _collected.Clear();

    void AddPoints(Player player, int key, PlanetProgress progress, int amount)
    {
        int before = Math.Max(progress.Tier, EarnedTier(progress.Points));
        progress = progress with { Points = progress.Points + amount };
        int after = Math.Max(progress.Tier, EarnedTier(progress.Points));
        Stores.Profiles.SetPlanet(player.ProfileId, key, progress);
        if (_session.Play)
            for (int tier = before + 1; tier <= after; tier++)
                Experience.Award(player, TierXp()[tier], TierUnlockedReward);
        Update(player);
    }

    public void Refresh(Player player) => Update(player);

    int? Key => _session.WorldId is int world ? _session.Play ? world : -world : null;

    void Update(Player player)
    {
        if (!Supports(player, EventCode.PlayerPlanetData)) return;
        player.Peer.Send(new EventData((byte)EventCode.PlayerPlanetData)
        {
            Parameters = { [(byte)ParameterKey.Data] = JsonSerializer.Serialize(PlanetData(player)) },
        });
    }

    public string HighScores(Player player, bool top)
    {
        List<PlanetScore> scores = Key is int key ? Stores.Profiles.PlanetScores(key) : [];
        int own = scores.FindIndex(score => score.Profile == player.ProfileId);
        int start = top || own < 0 ? 0 : Math.Max(0, Math.Min(own - ListMiddle, scores.Count - ListSize));
        return JsonSerializer.Serialize(new
        {
            highScores = scores.Skip(start).Take(ListSize).Select(score => new
            {
                profileID = score.Profile,
                username = score.Name ?? $"Player{score.Profile}",
                gamePoints = score.Points,
                isSubscriber = false,
            }),
            topRank = start + 1,
        });
    }

    int Rank(Player player, int key) => Stores.Profiles.PlanetScores(key).FindIndex(score => score.Profile == player.ProfileId) + 1;

    object PlanetData(Player player)
    {
        PlanetProgress progress = Key is int key ? Stores.Profiles.Planet(player.ProfileId, key) : new PlanetProgress(0, 0, 0, null);
        player.PlanetSummary = JsonSerializer.Serialize(new { highScoreGamePoints = progress.Points, gamePassTier = progress.Tier });
        return new
        {
            highScoreGamePoints = progress.Points,
            rank = Key is int ranked ? Rank(player, ranked) : 0,
            progressionGamePoints = progress.Points,
            playtime = "00:00:00",
            gamePassTier = progress.Tier,
            playerPlanetMetaData = new
            {
                gamePassTierSeen = progress.Seen,
                welcomeRewardClaimed = progress.Welcome is not null,
                lastDailyWelcomeRewardClaim = (progress.Welcome ?? DateTime.MinValue).ToString("yyyy-MM-ddTHH:mm:ss"),
            },
            previewGamePassTier = 0,
        };
    }

    int[] Requirements() => [.. TierXp().Select(xp => xp * 5 / 6)];

    int EarnedTier(int points)
    {
        int[] requirements = Requirements();
        int total = 0;
        int earned = 0;
        for (int tier = 0; tier <= MaxTier; tier++)
        {
            total += requirements[tier];
            if (points >= total) earned = tier;
            else break;
        }
        return earned;
    }

    int RemainingPrice(int points, int tier)
    {
        int[] requirements = Requirements();
        int price = TierXp()[tier] / 2;
        int before = requirements.Take(tier).Sum();
        int toward = Math.Clamp(points - before, 0, requirements[tier]);
        return requirements[tier] == 0 ? price : price - (int)((double)price * toward / requirements[tier]);
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
        return new { gamePassRewardsActivated = _enabled, gamePointVelocityIsZero = !_enabled, welcomeReward = WelcomeReward, progressionThresholds = thresholds };
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
