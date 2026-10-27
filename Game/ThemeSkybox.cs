using System.Text.Json;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class ThemeSkybox(Session session)
{
    const float ClassicFog = 0.007f;
    const long RefreshMs = 5000;

    static readonly Snapshot ThemeProbe = new([], [new WorldObject { Type = WorldObjectType.Theme }], [], []);
    static readonly Snapshot SkyboxProbe = new([], [new WorldObject { Type = WorldObjectType.Skybox }], [], []);
    static readonly List<List<SkyKey>> Palettes = Load();

    readonly object _sync = new();
    readonly HashSet<int> _shown = [];
    int _id;
    long _refreshed;

    public Snapshot AddTo(Snapshot snapshot, Player player, Dictionary<string, object> settings, float time)
    {
        bool wanted = Wants(player);
        lock (_sync)
        {
            if (!wanted)
            {
                _shown.Remove(player.Actor);
                return snapshot;
            }
            _shown.Add(player.Actor);
        }
        return snapshot with { Objects = [.. snapshot.Objects, Build(settings, time)] };
    }

    public void Update(Dictionary<string, object>? settings, float time)
    {
        foreach (Player player in session.Players.Where(player => player.InWorld))
        {
            bool wanted = settings is not null && Wants(player);
            bool shown;
            lock (_sync)
            {
                shown = _shown.Contains(player.Actor);
                if (wanted) _shown.Add(player.Actor);
                else _shown.Remove(player.Actor);
            }

            if (wanted && !shown)
                GetNextGameBatch.SendAdded(session, 0, new Snapshot([], [Build(settings!, time)], [], []), other => other == player);
            else if (wanted)
                player.Peer.Send(new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
                {
                    Parameters =
                    {
                        [(byte)ParameterKey.WorldObjectID] = Id,
                        [(byte)ParameterKey.WorldObjectData] = PackedData.ToPhoton(Data(settings!, time, session.Theme.Current?.Cycle)),
                    },
                });
            else if (shown)
                player.Peer.Send(new EventData((byte)EventCode.UnregisterWorldObject)
                {
                    Parameters = { [(byte)ParameterKey.WorldObjectID] = Id },
                });
        }
    }

    public bool Due()
    {
        long now = Environment.TickCount64;
        lock (_sync)
        {
            if (now - _refreshed < RefreshMs) return false;
            _refreshed = now;
            return true;
        }
    }

    bool Wants(Player player) =>
        session.Play
        && player.Peer.Translator is not Kogama.Protocols.LegacyTranslator
        && !player.Knows(ThemeProbe)
        && player.Knows(SkyboxProbe)
        && session.World.FindFirst(WorldObjectType.Skybox) is null;

    int Id
    {
        get
        {
            lock (_sync)
            {
                if (_id == 0) _id = session.World.NewObjectId();
                return _id;
            }
        }
    }

    WorldObject Build(Dictionary<string, object> settings, float time)
    {
        var skybox = new WorldObject
        {
            Id = Id,
            ParentId = session.World.RootId,
            Type = WorldObjectType.Skybox,
            Position = [.. session.World.Spawn],
            Data = Data(settings, time, session.Theme.Current?.Cycle),
        };
        RuntimeDefaults.Apply(skybox);
        return skybox;
    }

    static List<(string Key, PackedType Type, object Value)> Data(Dictionary<string, object> settings, float time, ThemeCycleData? cycle)
    {
        float[] top, bottom;
        float sunAngle;
        if (settings.GetValueOrDefault(ThemeValues.DayNightCycle) is true)
        {
            int palette = Math.Clamp(settings.GetValueOrDefault("colorPresetIndex") as int? ?? 0, 0, Palettes.Count - 1);
            (top, bottom) = At(palette == 0 && cycle is { Sky.Count: > 0 } ? cycle.Sky : Palettes[palette], time);
            float sunrise = cycle?.Sunrise ?? ThemeCycleData.GameSunrise;
            float day = ThemeCycleData.Wrap((cycle?.Sunset ?? ThemeCycleData.GameSunset) - sunrise);
            float since = ThemeCycleData.Wrap(time - sunrise);
            sunAngle = since <= day ? ThemeCycleData.OrbitStart + (ThemeCycleData.OrbitEnd - ThemeCycleData.OrbitStart) * since / day : ThemeCycleData.OrbitStart;
        }
        else
        {
            top = settings.GetValueOrDefault("topColor") as float[] ?? [1f, 1f, 1f, 1f];
            bottom = settings.GetValueOrDefault("bottomColor") as float[] ?? [1f, 1f, 1f, 1f];
            sunAngle = (settings.GetValueOrDefault("sunHeight") as float? ?? 0.5f) * 90f;
        }

        float exposure = settings.GetValueOrDefault("exposure") as float? ?? 1f;
        float[] color = [.. Enumerable.Range(0, 3).Select(i => Math.Clamp((top[i] + bottom[i]) / 2f * exposure, 0f, 1f))];
        float fog = settings.GetValueOrDefault("useFog") is true ? settings.GetValueOrDefault("fogDensity") as float? ?? ClassicFog : ClassicFog;
        return
        [
            ("sunAngle", PackedType.Single, sunAngle),
            ("color", PackedType.SingleArray, color),
            ("fogDensity", PackedType.Single, fog),
        ];
    }

    static (float[] Top, float[] Bottom) At(IReadOnlyList<SkyKey> keys, float time)
    {
        int next = -1;
        for (int i = 0; i < keys.Count && next < 0; i++)
            if (keys[i].Time > time) next = i;
        SkyKey after = keys[next < 0 ? 0 : next];
        SkyKey before = keys[next < 1 ? keys.Count - 1 : next - 1];
        float span = after.Time > before.Time ? after.Time - before.Time : 100f + after.Time - before.Time;
        float offset = time >= before.Time ? time - before.Time : time + 100f - before.Time;
        float t = span > 0f ? Math.Clamp(offset / span, 0f, 1f) : 0f;
        return (Lerp(before.Top, after.Top, t), Lerp(before.Bottom, after.Bottom, t));
    }

    static float[] Lerp(float[] from, float[] to, float t) => [.. from.Select((value, i) => value + (to[i] - value) * t)];

    static List<List<SkyKey>> Load()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "theme-palettes.json")));
        return [.. json.RootElement.EnumerateArray().Select(palette => palette.GetProperty("sky").EnumerateArray()
            .Select(key => new SkyKey(
                key.GetProperty("time").GetSingle(),
                [.. key.GetProperty("top").EnumerateArray().Select(channel => channel.GetSingle())],
                [.. key.GetProperty("bottom").EnumerateArray().Select(channel => channel.GetSingle())]))
            .OrderBy(key => key.Time)
            .ToList())];
    }
}
