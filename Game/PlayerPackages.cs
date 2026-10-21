using System.IO.Compression;
using System.Text.Json;
using OpenKogama.Api;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public sealed record PackageItem(int Id, string Name, int Category, string Data);

public sealed record PackageInventory(List<PackageItem>? Items, Dictionary<int, int>? Slots);

public sealed record PlayerPackage(JsonElement? Settings, AvatarSkin? Avatar, PackageInventory? Inventory);

public static class PlayerPackages
{
    public const int MaxBytes = 16 * 1024 * 1024;
    const int MaxItems = 500;
    const int MaxItemBytes = 1024 * 1024;
    const int MaxSlots = 5000;
    const int MaxAccessories = 50;
    const int MaxNameLength = 100;
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    static readonly Dictionary<string, (PlayerPackage Package, DateTime At)> Waiting = [];
    static readonly Lazy<HashSet<int>> Catalog = new(() => [.. Items.Catalog.Items.Select(item => item.Id)]);

    public static bool Offer(string? key, byte[] body, string? encoding = null)
    {
        if (encoding is "deflate" && Inflated(body) is byte[] inflated) body = inflated;
        else if (encoding is not null) return false;
        if (ProfileSettings.CleanKey(key) is not string clean || body.Length > MaxBytes) return false;
        PlayerPackage? package;
        try
        {
            package = JsonSerializer.Deserialize<PlayerPackage>(body, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return false;
        }
        if (package is null) return false;

        DateTime now = DateTime.UtcNow;
        lock (Waiting)
        {
            foreach (string old in Waiting.Where(entry => now - entry.Value.At > Lifetime).Select(entry => entry.Key).ToList())
                Waiting.Remove(old);
            Waiting[clean] = (package, now);
        }
        return true;
    }

    public static PlayerPackage? Find(string? key)
    {
        if (ProfileSettings.CleanKey(key) is not string clean) return null;
        lock (Waiting)
            return Waiting.TryGetValue(clean, out var entry) && DateTime.UtcNow - entry.At <= Lifetime ? entry.Package : null;
    }

    public static void Apply(int profile, PlayerPackage package, bool avatars, bool inventory)
    {
        Dictionary<int, int> brought = inventory && package.Inventory is { } given ? Bring(profile, given) : [];
        if (avatars && package.Avatar is { } skin)
            AvatarFiles.Wear(profile, skin with { Accessories = [.. (skin.Accessories ?? []).Take(MaxAccessories)] }, item =>
                brought.TryGetValue(item, out int mapped) ? mapped : Catalog.Value.Contains(item) ? item : null);
        Console.WriteLine($"profile {profile}: took{(avatars && package.Avatar is not null ? " avatar" : "")}{(brought.Count > 0 ? $" {brought.Count} items" : "")} from the player");
    }

    public static string Export(int profile)
    {
        var inventory = new PackageInventory(
            [.. Stores.Profiles.Items(profile)
                .Where(item => item.Category == ModelInventory.ModelCategory)
                .Select(item => new PackageItem(item.Id, item.Name, item.Category, item.Data))],
            Stores.Profiles.Slots(profile));
        return JsonSerializer.Serialize(new PlayerPackage(null, AvatarFiles.Export(Stores.Profiles.ActiveAvatar(profile)), inventory), Options);
    }

    static Dictionary<int, int> Bring(int profile, PackageInventory inventory)
    {
        List<Item> owned = Stores.Profiles.Items(profile);
        var brought = new Dictionary<int, int>();
        foreach (PackageItem item in (inventory.Items ?? []).Take(MaxItems))
        {
            if (item.Category != ModelInventory.ModelCategory || brought.ContainsKey(item.Id) || Model(item.Data) is not byte[] bytes) continue;
            string data = Convert.ToBase64String(bytes);
            Item? same = owned.Find(mine => mine.Category == item.Category && mine.Data == data);
            if (same is null)
            {
                same = Inventories.Add(profile, Name(item.Name), item.Category, bytes, profile);
                owned.Add(same);
            }
            brought[item.Id] = same.Id;
        }

        Dictionary<int, int> slots = Stores.Profiles.Slots(profile);
        foreach ((int item, int slot) in (inventory.Slots ?? []).Take(MaxSlots))
        {
            int target = brought.TryGetValue(item, out int mapped) ? mapped : Catalog.Value.Contains(item) ? item : 0;
            if (target > 0 && slot >= 0) slots[target] = slot;
        }
        Stores.Profiles.SetSlots(profile, slots);
        return brought;
    }

    static byte[]? Inflated(byte[] packed)
    {
        try
        {
            using var input = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            for (int read; (read = input.Read(buffer)) > 0;)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxBytes) return null;
            }
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    static byte[]? Model(string? data)
    {
        if (data is null || data.Length > MaxItemBytes * 4 / 3 + 4) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(data);
            return ModelInventory.SinglePrototype(new Item { Category = ModelInventory.ModelCategory, Data = data }) is not null ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    static string Name(string? name)
    {
        string clean = name?.Trim() ?? "";
        return clean.Length == 0 ? ModelInventory.ModelName : clean[..Math.Min(clean.Length, MaxNameLength)];
    }
}
