using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenKogama.World;

public static class TextEras
{
    const float FontSizeConversion = 10f;
    const float DefaultTextSize = 0.2f;
    const float BaseFontSize = 100f;

    static readonly Version DataVersion2 = new(3, 5, 13);
    static readonly Version TextMeshProVersion = new(3, 2, 9);
    static readonly Regex Size = new(@"<size=(\d+(?:\.\d+)?)>");
    static readonly Regex SizeSpan = new(@"<size=(\d+(?:\.\d+)?)>(.*?)</size>", RegexOptions.Singleline);
    static readonly Regex AnyTag = new(@"<[^>]*>");
    static readonly HashSet<string> Version2Keys =
    [
        "version", "fontSelection", "textThickness", "textOutline", "textOutlineThickness", "textOutlineColor",
        "textItalic", "textUnderscored", "background", "backgroundWidth", "backgroundColor", "backgroundRadius",
        "backgroundOutline", "backgroundOutlineThickness", "backgroundOutlineColor", "billboard",
    ];

    public static void ForClient(WorldObject obj, string? client)
    {
        if (obj.Type != WorldObjectType.TextMsg) return;
        Version? target = Version.TryParse(client, out Version? parsed) ? parsed : null;

        string text = obj.Data.Find(pair => pair.Key == "text").Value as string ?? "";
        if (Before(target, DataVersion2) && obj.Data.Find(pair => pair.Key == "version").Value is 2)
        {
            float size = obj.Data.Find(pair => pair.Key == "textSize").Value as float? ?? DefaultTextSize;
            if (size > 0)
                text = Size.Replace(text, match =>
                    $"<size={Math.Max(1, (int)Math.Round(float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * FontSizeConversion / size))}>");
            obj.Data.RemoveAll(pair => Version2Keys.Contains(pair.Key));
        }

        if (Before(target, TextMeshProVersion) && SizeSpan.IsMatch(text))
        {
            float dominant = SizeSpan.Matches(text)
                .GroupBy(match => float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                .MaxBy(group => group.Sum(match => AnyTag.Replace(match.Groups[2].Value, "").Trim().Length))!.Key;
            float size = obj.Data.Find(pair => pair.Key == "textSize").Value as float? ?? DefaultTextSize;
            obj.Data.RemoveAll(pair => pair.Key == "textSize");
            obj.Data.Add(("textSize", PackedType.Single, size * dominant / BaseFontSize));
            text = Size.Replace(text, "").Replace("</size>", "");
        }

        SetText(obj.Data, text);
    }

    static bool Before(Version? target, Version version) => target is null || target < version;

    static void SetText(List<(string Key, PackedType Type, object Value)> data, string text)
    {
        int index = data.FindIndex(pair => pair.Key == "text");
        if (index >= 0) data[index] = ("text", PackedType.String, text);
    }
}
