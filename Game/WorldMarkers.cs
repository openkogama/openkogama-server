using System.Text.RegularExpressions;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class WorldMarkers
{
    const string Text = "text";
    const string TextSize = "textSize";
    static readonly Regex Tags = new("<[^>]*>");
    static readonly Regex Marker = new(@"^\$\$([A-Za-z0-9_-]+)$");

    public static string? NameOf(WorldObject obj) =>
        obj.Type == WorldObjectType.TextMsg
        && obj.Data.Find(pair => pair.Key == Text).Value is string text
        && Marker.Match(Tags.Replace(text, "").Trim()) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    public static List<WorldObject> Named(GameWorld world, string name) =>
        [.. world.ToSnapshot().Objects.Where(obj => string.Equals(NameOf(obj), name, StringComparison.OrdinalIgnoreCase))];

    public static void Hide(GameWorld world)
    {
        foreach (WorldObject marker in world.ToSnapshot().Objects.Where(obj => NameOf(obj) is not null))
            world.Modify(marker.Id, obj => PackedData.Merge(obj.Data, new Dictionary<object, object?> { [TextSize] = 0f }));
    }
}
