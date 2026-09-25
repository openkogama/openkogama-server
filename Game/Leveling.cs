using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public sealed class XpType
{
    public string Type { get; set; } = "";
    public int Id { get; set; }
    public int Amount { get; set; }
}

public sealed class LevelInfo
{
    public int Xp { get; set; }
    public int Friends { get; set; }
}

public sealed class LevelingData
{
    public int MinPlayersActivateXP { get; set; } = 2;
    public List<XpType> Xp { get; set; } = [];
    public List<LevelInfo> Levels { get; set; } = [new()];
}

public static class Leveling
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static LevelingData? _data;

    public static LevelingData Data => _data ??= JsonSerializer.Deserialize<LevelingData>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "leveling.json")), Options) ?? new();

    public static int MaxLevel => Data.Levels.Count;

    public static int XpOf(int profile) => Stores.Profiles.Xp(profile);

    public static int LevelOf(int profile) => LevelFor(XpOf(profile));

    public static int LevelFor(int xp) => Data.Levels.Count(level => xp >= level.Xp);

    public static JsonObject Limits(int level)
    {
        level = Math.Clamp(level, 1, MaxLevel);
        int prev = Data.Levels[level - 1].Xp;
        int next = level < MaxLevel ? Data.Levels[level].Xp : int.MaxValue;
        return new JsonObject { ["PrevXP"] = prev, ["NextXP"] = next, ["Level"] = level };
    }

    public static int Add(int profile, int typeId)
    {
        XpType? type = Data.Xp.Find(xp => xp.Id == typeId);
        return Stores.Profiles.AddXp(profile, type?.Amount ?? 0);
    }

    public static JsonObject InitData(int profile, string badgeRoot)
    {
        int xp = XpOf(profile);
        var xpTypes = new JsonObject();
        foreach (XpType type in Data.Xp)
            xpTypes[type.Type] = new JsonObject { ["XPId"] = type.Id, ["XPAmount"] = type.Amount };

        var badges = new JsonArray();
        for (int level = 1; level <= MaxLevel; level++)
            badges.Add(new JsonObject { ["Level"] = level, ["URL"] = $"{badgeRoot}{level}.png", ["FriendsLimit"] = Data.Levels[level - 1].Friends });

        return new JsonObject
        {
            ["XPManagerData"] = xpTypes,
            ["BadgeUrlData"] = badges,
            ["Level"] = LevelFor(xp),
            ["XP"] = xp,
            ["XPLevelLimits"] = Limits(LevelFor(xp)),
            ["MinPlayersActivateXP"] = Data.MinPlayersActivateXP,
        };
    }
}
