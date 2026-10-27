using System.Buffers.Binary;
using System.Text;
using OpenKogama.Game;

namespace OpenKogama.Content;

public sealed record ThemeSpec(
    string Name,
    string Base,
    string Title,
    string Description,
    IReadOnlyDictionary<string, object> Settings,
    IReadOnlyDictionary<string, string> Textures,
    ThemeCycleData? Cycle = null);

public static class ThemePatch
{
    const int GameObjectClass = 1;
    const int TransformClass = 4;
    const int RectTransformClass = 224;
    const int PPtrSize = 12;
    const int ChildrenAt = 52;
    const int NameAt = 28;
    const string Repository = "ThemeRepository";
    const string ColorPresets = "ColorPresets";
    const string Prefix = "Themes/";
    const int CycleSize = 72;
    const int MoonriseAt = 24;
    const int PresetsAt = 60;
    const int SkyKeySize = 36;
    const int LightKeySize = 40;
    const int StarsKeySize = 20;

    static readonly (string Key, ThemeValueKind Kind)[] Attributes =
    [
        ("useDayNightCycle", ThemeValueKind.Bool), ("useServerTime", ThemeValueKind.Bool), ("colorPresetIndex", ThemeValueKind.Int),
        ("gameStartTime", ThemeValueKind.Float), ("cycleLength", ThemeValueKind.Float), ("sunAltitude", ThemeValueKind.Float),
        ("sunLongitude", ThemeValueKind.Float), ("moonAltitude", ThemeValueKind.Float), ("moonLongitude", ThemeValueKind.Float),
        ("topColor", ThemeValueKind.Color), ("bottomColor", ThemeValueKind.Color), ("starsTint", ThemeValueKind.Color),
        ("starsExtinction", ThemeValueKind.Float), ("starsTwinklingSpeed", ThemeValueKind.Float), ("sunTint", ThemeValueKind.Color),
        ("sunSize", ThemeValueKind.Float), ("sunHeight", ThemeValueKind.Float), ("sunAxis", ThemeValueKind.Float),
        ("sunLightContrast", ThemeValueKind.Float), ("sunLightIntensity", ThemeValueKind.Float), ("sunFlareBrightness", ThemeValueKind.Float),
        ("moonTint", ThemeValueKind.Color), ("moonSize", ThemeValueKind.Float), ("moonHeight", ThemeValueKind.Float),
        ("moonAxis", ThemeValueKind.Float), ("moonLightContrast", ThemeValueKind.Float), ("moonLightIntensity", ThemeValueKind.Float),
        ("moonFlareBrightness", ThemeValueKind.Float), ("cloudsHeight", ThemeValueKind.Float), ("cloudsOffset", ThemeValueKind.Float),
        ("cloudsRotationSpeed", ThemeValueKind.Float), ("useFog", ThemeValueKind.Bool), ("fogDensity", ThemeValueKind.Float),
        ("exposure", ThemeValueKind.Float),
    ];

    sealed record Layout(int Body, int Button, Dictionary<string, int> Values, int Strings, string Identifier, string Title, string Description);

    public static bool Holds(byte[] file) => file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Repository)) >= 0 && FindRepository(new SerializedFile(file)) is not null;

    public static byte[] Apply(byte[] bytes, IReadOnlyList<ThemeSpec> themes, out List<string> applied)
    {
        applied = [];
        var file = new SerializedFile(bytes);
        SerializedFile.Entry repository = FindRepository(file) ?? throw new InvalidDataException("the client has no theme list this patch knows");
        byte[] list = file.Data(repository).ToArray();
        int listAt = Header(list);
        int count = Int(list, listAt);
        if (listAt + 4 + count * PPtrSize != list.Length) throw new InvalidDataException("the theme list has an unknown layout");

        var prefabs = new Dictionary<string, (SerializedFile.Entry Entry, Layout Layout)>();
        var pointers = new List<long>();
        for (int i = 0; i < count; i++)
        {
            int at = listAt + 4 + i * PPtrSize;
            long pathId = Long(list, at + 4);
            pointers.Add(pathId);
            if (Int(list, at) != 0 || file.Find(pathId) is not { } entry) continue;
            if (Read(file.Data(entry).ToArray()) is { } layout) prefabs[layout.Identifier] = (entry, layout);
        }
        if (prefabs.Count == 0) throw new InvalidDataException("the theme list has no themes this patch can read");

        var changed = new Dictionary<long, byte[]>();
        var added = new List<(long PathId, long Like, byte[] Data)>();
        long next = file.NextPathId;
        SerializedFile.Entry? presets = FindNamed(file, ColorPresets);

        byte[]? Recycle(byte[] data, ThemeSpec spec)
        {
            if (spec.Cycle is not { } cycle || presets is null || !IsCycle(data, presets.PathId)) return null;
            byte[] palette;
            try
            {
                palette = Palette(file.Data(presets).ToArray(), cycle, spec.Name);
            }
            catch (Exception error) when (error is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
            {
                return null;
            }
            long id = next++;
            added.Add((id, presets.PathId, palette));
            return Cycle(data, cycle, id);
        }

        foreach (ThemeSpec spec in themes)
        {
            if (Themes.Find(spec.Base) is not { } builtIn || !prefabs.TryGetValue(spec.Base, out var basis)) continue;
            Dictionary<string, string> urls = spec.Textures
                .Where(pair => builtIn.Textures.ContainsKey(pair.Key))
                .ToDictionary(pair => Prefix + builtIn.Folder + "/" + builtIn.Textures[pair.Key], pair => pair.Value);

            if (spec.Name == spec.Base)
            {
                if (urls.Count == 0 && spec.Cycle is null) continue;
                foreach (long id in Hierarchy(file, Owner(file, basis.Entry)).Concat(Button(file, basis.Layout, basis.Entry)))
                {
                    if (file.Find(id) is not { ClassId: SerializedFile.MonoBehaviourClass } component) continue;
                    byte[] data = file.Data(component).ToArray();
                    if ((Retarget(data, urls) ?? Recycle(data, spec)) is { } edited) changed[id] = edited;
                }
                applied.Add(spec.Name);
                continue;
            }
            if (prefabs.ContainsKey(spec.Name) || applied.Contains(spec.Name)) continue;

            List<long> objects = Hierarchy(file, Owner(file, basis.Entry));
            if (spec.Textures.ContainsKey("preview")) objects.AddRange(Button(file, basis.Layout, basis.Entry));
            var renamed = objects.Distinct().ToDictionary(id => id, _ => next++);

            foreach ((long old, long fresh) in renamed)
            {
                SerializedFile.Entry entry = file.Find(old)!;
                byte[] data = Remap(file, entry, renamed);
                if (old == basis.Entry.PathId) data = Customize(data, spec);
                else if (entry.ClassId == SerializedFile.MonoBehaviourClass && (Retarget(data, urls) ?? Recycle(data, spec)) is { } edited) data = edited;
                added.Add((fresh, old, data));
            }
            pointers.Add(renamed[basis.Entry.PathId]);
            applied.Add(spec.Name);
        }
        if (applied.Count == 0) return bytes;

        byte[] grown = new byte[listAt + 4 + pointers.Count * PPtrSize];
        list.AsSpan(0, listAt).CopyTo(grown);
        BinaryPrimitives.WriteInt32LittleEndian(grown.AsSpan(listAt), pointers.Count);
        for (int i = 0; i < pointers.Count; i++)
            BinaryPrimitives.WriteInt64LittleEndian(grown.AsSpan(listAt + 4 + i * PPtrSize + 4), pointers[i]);
        changed[repository.PathId] = grown;
        return file.Rebuild(changed, added);
    }

    static SerializedFile.Entry? FindRepository(SerializedFile file) => FindNamed(file, Repository);

    static SerializedFile.Entry? FindNamed(SerializedFile file, string name)
    {
        foreach (SerializedFile.Entry entry in file.Objects)
        {
            if (entry.ClassId != SerializedFile.MonoBehaviourClass || entry.Size < 32 + name.Length) continue;
            ReadOnlySpan<byte> data = file.Data(entry);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[NameAt..]);
            if (length == name.Length && data.Slice(NameAt + 4, length).SequenceEqual(Encoding.ASCII.GetBytes(name))) return entry;
        }
        return null;
    }

    static bool IsCycle(byte[] data, long presets)
    {
        try
        {
            int body = Header(data);
            return data.Length == body + CycleSize && Int(data, body + PresetsAt) == 0 && Long(data, body + PresetsAt + 4) == presets;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidDataException)
        {
            return false;
        }
    }

    static byte[] Cycle(byte[] data, ThemeCycleData cycle, long presets)
    {
        int body = Header(data);
        void Write(int at, float? value)
        {
            if (value is { } number) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(body + at), number);
        }
        Write(0, cycle.Sunrise);
        Write(4, cycle.Sunset);
        Write(MoonriseAt, cycle.Moonrise);
        Write(MoonriseAt + 4, cycle.Moonset);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(body + PresetsAt + 4), presets);
        return data;
    }

    static byte[] Palette(byte[] data, ThemeCycleData cycle, string name)
    {
        int body = Header(data);
        int count = Int(data, body);
        var presets = new List<int[]>();
        int at = body + 4;
        for (int i = 0; i < count; i++)
        {
            int[] parts = new int[6];
            parts[0] = at;
            parts[1] = at = SkipText(data, at);
            parts[2] = at = SkipKeys(data, at, SkyKeySize);
            parts[3] = at = SkipKeys(data, at, LightKeySize);
            parts[4] = at = SkipKeys(data, at, LightKeySize);
            parts[5] = at = SkipKeys(data, at, StarsKeySize);
            presets.Add(parts);
        }
        if (count < 1 || at != data.Length) throw new InvalidDataException("the color presets have an unknown layout");

        using var output = new MemoryStream();
        output.Write(data, 0, NameAt);
        WriteText(output, $"{ColorPresets} {name}");
        output.Write(data, body, 4);
        int[] first = presets[0];
        output.Write(data, first[0], first[1] - first[0]);
        void Part(int index, bool own, Action write)
        {
            if (own) write();
            else output.Write(data, first[index], first[index + 1] - first[index]);
        }
        Part(1, cycle.Sky.Count > 0, () => WriteKeys(output, cycle.Sky, key => [key.Time, .. key.Top, .. key.Bottom]));
        Part(2, cycle.Sun.Count > 0, () => WriteKeys(output, cycle.Sun, key => [key.Time, .. key.Tint, .. key.Light, key.Intensity]));
        Part(3, cycle.Moon.Count > 0, () => WriteKeys(output, cycle.Moon, key => [key.Time, .. key.Tint, .. key.Light, key.Intensity]));
        Part(4, cycle.Stars.Count > 0, () => WriteKeys(output, cycle.Stars, key => [key.Time, .. key.Tint]));
        output.Write(data, first[5], data.Length - first[5]);
        return output.ToArray();
    }

    static int SkipKeys(byte[] data, int at, int size)
    {
        int count = Int(data, at);
        if (count < 0 || at + 4 + (long)count * size > data.Length) throw new InvalidDataException("bad key list");
        return at + 4 + count * size;
    }

    static void WriteKeys<T>(Stream output, IReadOnlyList<T> keys, Func<T, float[]> values)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, keys.Count);
        output.Write(number);
        foreach (T key in keys)
            foreach (float value in values(key))
            {
                BinaryPrimitives.WriteSingleLittleEndian(number, value);
                output.Write(number);
            }
    }

    static Layout? Read(byte[] data)
    {
        try
        {
            int body = Header(data);
            int at = body + 4;
            int button = at;
            at += PPtrSize;
            var values = new Dictionary<string, int>();
            foreach ((string key, ThemeValueKind kind) in Attributes)
            {
                values[key] = at;
                at += kind == ThemeValueKind.Color ? 16 : 4;
                at = SkipText(data, at);
                if (kind is ThemeValueKind.Int or ThemeValueKind.Float) at += 8;
                at += PPtrSize;
            }
            at += 3 * PPtrSize;
            int strings = at;
            (string identifier, at) = Text(data, at);
            (string title, at) = Text(data, at);
            (string description, at) = Text(data, at);
            return at == data.Length ? new Layout(body, button, values, strings, identifier, title, description) : null;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    static byte[] Customize(byte[] data, ThemeSpec spec)
    {
        Layout layout = Read(data) ?? throw new InvalidDataException("the theme prefab has an unknown layout");
        foreach ((string key, object value) in spec.Settings)
        {
            if (!layout.Values.TryGetValue(key, out int at)) continue;
            switch (value)
            {
                case bool flag:
                    data[at] = flag ? (byte)1 : (byte)0;
                    break;
                case int number:
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at), number);
                    break;
                case float number:
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at), number);
                    break;
                case float[] color:
                    for (int c = 0; c < 4; c++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + c * 4), color[c]);
                    break;
            }
        }

        using var output = new MemoryStream();
        output.Write(data, 0, layout.Strings);
        WriteText(output, spec.Name);
        WriteText(output, $"_(\"{spec.Title.Replace('"', '\'')}\")");
        WriteText(output, $"_(\"{spec.Description.Replace('"', '\'')}\")");
        return output.ToArray();
    }

    static byte[]? Retarget(byte[] data, IReadOnlyDictionary<string, string> urls)
    {
        try
        {
            int body = Header(data);
            (string url, int end) = Text(data, body);
            if (!urls.TryGetValue(url, out string? replacement)) return null;
            using var output = new MemoryStream();
            output.Write(data, 0, body);
            WriteText(output, replacement);
            output.Write(data, end, data.Length - end);
            return output.ToArray();
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    static long Owner(SerializedFile file, SerializedFile.Entry component) => Long(file.Data(component).ToArray(), 4);

    static IEnumerable<long> Button(SerializedFile file, Layout layout, SerializedFile.Entry theme)
    {
        byte[] data = file.Data(theme).ToArray();
        if (Int(data, layout.Button) != 0 || file.Find(Long(data, layout.Button + 4)) is not { } button) return [];
        return Hierarchy(file, Owner(file, button));
    }

    static List<long> Hierarchy(SerializedFile file, long gameObject)
    {
        var objects = new List<long>();
        Collect(file, gameObject, objects);
        return objects;
    }

    static void Collect(SerializedFile file, long gameObject, List<long> objects)
    {
        if (file.Find(gameObject) is not { ClassId: GameObjectClass } entry) throw new InvalidDataException($"object {gameObject} is not a game object");
        objects.Add(gameObject);
        foreach (long component in Components(file, file.Data(entry).ToArray()).Ids)
        {
            objects.Add(component);
            if (file.Find(component) is not { ClassId: TransformClass or RectTransformClass } transform) continue;
            byte[] data = file.Data(transform).ToArray();
            int children = Int(data, ChildrenAt);
            for (int i = 0; i < children; i++)
            {
                long child = Long(data, ChildrenAt + 4 + i * PPtrSize + 4);
                if (file.Find(child) is { } childTransform) Collect(file, Owner(file, childTransform), objects);
            }
        }
    }

    static (List<long> Ids, int Stride) Components(SerializedFile file, byte[] gameObject)
    {
        int count = Int(gameObject, 0);
        foreach (int stride in new[] { PPtrSize, PPtrSize + 4 })
        {
            var components = new List<long>();
            for (int i = 0; i < count; i++)
            {
                int at = 4 + i * stride + (stride - PPtrSize);
                if (at + PPtrSize > gameObject.Length || Int(gameObject, at) != 0) break;
                long id = Long(gameObject, at + 4);
                if (file.Find(id) is not { } component || component.ClassId == GameObjectClass) break;
                components.Add(id);
            }
            if (components.Count == count) return (components, stride);
        }
        throw new InvalidDataException("a game object has an unknown layout");
    }

    static byte[] Remap(SerializedFile file, SerializedFile.Entry entry, IReadOnlyDictionary<long, long> renamed)
    {
        byte[] data = file.Data(entry).ToArray();
        void Point(int at)
        {
            if (at + PPtrSize <= data.Length && Int(data, at) == 0 && renamed.TryGetValue(Long(data, at + 4), out long fresh))
                BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(at + 4), fresh);
        }

        switch (entry.ClassId)
        {
            case GameObjectClass:
                (List<long> ids, int stride) = Components(file, data);
                for (int i = 0; i < ids.Count; i++) Point(4 + i * stride + (stride - PPtrSize));
                break;
            case TransformClass or RectTransformClass:
                Point(0);
                int children = Int(data, ChildrenAt);
                for (int i = 0; i < children; i++) Point(ChildrenAt + 4 + i * PPtrSize);
                Point(ChildrenAt + 4 + children * PPtrSize);
                break;
            case SerializedFile.MonoBehaviourClass:
                for (int at = 0; at + PPtrSize <= data.Length; at += 4)
                    if (Int(data, at) == 0 && renamed.ContainsKey(Long(data, at + 4)))
                    {
                        Point(at);
                        at += PPtrSize - 4;
                    }
                break;
            default:
                Point(0);
                break;
        }
        return data;
    }

    static int Header(byte[] data) => SkipText(data, NameAt);

    static int SkipText(byte[] data, int at)
    {
        int length = Int(data, at);
        if (length < 0 || at + 4 + length > data.Length) throw new InvalidDataException("bad string");
        return (at + 4 + length + 3) / 4 * 4;
    }

    static (string Text, int End) Text(byte[] data, int at) =>
        (Encoding.UTF8.GetString(data, at + 4, Int(data, at)), SkipText(data, at));

    static void WriteText(Stream output, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        output.Write(length);
        output.Write(bytes);
        output.Write(new byte[(4 - bytes.Length % 4) % 4]);
    }

    static int Int(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));

    static long Long(byte[] data, int at) => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at));
}
