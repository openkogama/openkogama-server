using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Mods.Patches;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class WorldTheme
{
    public const short Bought = 0;
    public const short InsufficientFunds = 1;
    public const short NotSold = 3;
    public const short Failed = 4;
    public const short InsufficientLevel = 6;
    const string IdentifierKey = "identifier";
    const string SettingsKey = "settings";
    const string BaseKey = "base";
    const double ServerTimeZone = 2.5;

    readonly Session _session;
    readonly ThemeSkybox _skybox;
    readonly object _sync = new();
    readonly Dictionary<int, float> _sentTime = [];
    float _clockTime;
    long _clockStart;

    public WorldTheme(Session session)
    {
        _session = session;
        _skybox = new ThemeSkybox(session);
        RestartClock(Settings);
    }

    public WorldObject? Object => _session.World.FindFirst(WorldObjectType.Theme);

    public ThemeEntry? Current => Object is { } obj ? Themes.Find(Identifier(obj)) : null;

    public static bool KnowsCustom(Player player) => Themes.Version is { Length: > 0 } version && Reauths.ThemesOf(player.Token) == version;

    public Dictionary<string, object>? Settings => Object is { } obj ? Effective(obj) : null;

    public float Time
    {
        get
        {
            if (Settings is not { } settings) return 0f;
            if (Flag(settings, ThemeKeys.ServerTime))
            {
                DateTime now = DateTime.UtcNow.AddHours(ServerTimeZone);
                return (float)(now.TimeOfDay.TotalMilliseconds / TimeSpan.FromDays(1).TotalMilliseconds * 100.0);
            }
            return SentTime(settings);
        }
    }

    public void SetTime(float time)
    {
        lock (_sync)
        {
            _clockTime = Wrap(time);
            _clockStart = Environment.TickCount64;
        }
        if (Object is { } obj) Broadcast(obj.Id, Effective(obj));
    }

    public void Set(ThemeEntry theme, IReadOnlyDictionary<string, object>? overrides)
    {
        Dictionary<string, object> settings = Merge(theme.Defaults, overrides ?? new Dictionary<string, object>());

        RemoveAll();
        GameWorld world = _session.World;
        var obj = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = world.RootId,
            Type = WorldObjectType.Theme,
            Data =
            [
                (IdentifierKey, PackedType.String, theme.Name),
                (SettingsKey, PackedType.Hashtable, PackedData.FromPhoton(ThemeValues.ToTable(settings))),
            ],
        };
        if (theme.Custom) obj.Data.Add((BaseKey, PackedType.String, theme.Base));
        world.Add(obj);
        world.MarkChanged();
        RestartClock(settings);

        float time = SentTime(settings);
        Remember(_session.Players, time);
        GetNextGameBatch.SendAdded(_session, 0, new Snapshot([], [Wire(obj, settings, time, custom: true)], [], []), KnowsCustom);
        GetNextGameBatch.SendAdded(_session, 0, new Snapshot([], [Wire(obj, settings, time, custom: false)], [], []), player => !KnowsCustom(player));
        _skybox.Update(settings, Time);
        Console.WriteLine($"world {_session.WorldId}: theme {theme.Name}");
    }

    public bool Change(IReadOnlyDictionary<string, object> changes)
    {
        if (Object is not { } obj) return false;
        Dictionary<string, object> before = Effective(obj);
        Dictionary<string, object> after = Merge(before, changes);
        Store(obj.Id, after);
        Retime(before, after, changes.ContainsKey(ThemeKeys.StartTime));
        Broadcast(obj.Id, after);
        return true;
    }

    public bool Remove() => RemoveAll() > 0;

    public short Buy(Player player, int id, Dictionary<object, object?> table)
    {
        if (_session.Play) return Failed;
        if (Themes.Find(id) is not { InMenu: true } theme || theme.Custom && !KnowsCustom(player)) return NotSold;
        if (player.Level < theme.Level) return InsufficientLevel;
        if (theme.Price > 0 && Stores.Profiles.Gold(player.ProfileId) < theme.Price) return InsufficientFunds;

        Dictionary<string, object> settings = Merge(theme.Defaults, ThemeValues.Read(table));
        if (Plugins.PluginHost.ThemeChanging(_session, player, theme, settings)) return Failed;
        if (theme.Price > 0) Stores.Profiles.AddGold(player.ProfileId, -theme.Price);
        Set(theme, settings);
        return Bought;
    }

    public void Edited(Player player, int objectId, Dictionary<object, object?> change)
    {
        if (_session.World.Find(objectId) is not { Type: WorldObjectType.Theme } obj) return;

        Dictionary<string, object> before = Effective(obj);
        Dictionary<string, object> sent = ThemeValues.Read(change.GetValueOrDefault(SettingsKey));
        bool startChanged;
        lock (_sync)
            startChanged = sent.TryGetValue(ThemeKeys.StartTime, out object? start)
                && !(_sentTime.TryGetValue(player.Actor, out float last) && start is float value && value == last);
        if (!startChanged) sent.Remove(ThemeKeys.StartTime);
        Dictionary<string, object> after = Merge(before, sent);

        if (Plugins.PluginHost.ThemeChanging(_session, player, Themes.Find(Identifier(obj)), after))
        {
            Send(obj.Id, before, [player]);
            return;
        }
        Store(obj.Id, after);
        Retime(before, after, startChanged);
        Broadcast(obj.Id, after);
    }

    public bool Removing(Player player) => !Plugins.PluginHost.ThemeChanging(_session, player, null, null);

    public Snapshot ForPlayer(Snapshot snapshot, Player player)
    {
        if (Object is not { } theme) return snapshot;
        Dictionary<string, object> settings = Effective(theme);
        List<WorldObject> objects = [.. snapshot.Objects];
        int index = objects.FindIndex(obj => obj.Id == theme.Id);
        if (index >= 0)
        {
            float time = SentTime(settings);
            Remember([player], time);
            objects[index] = Wire(objects[index], settings, time, KnowsCustom(player));
        }
        return _skybox.AddTo(snapshot with { Objects = objects }, player, settings, Time);
    }

    public void Tick()
    {
        if (_session.Play && _skybox.Due()) _skybox.Update(Settings, Time);
    }

    public void RoundStarted()
    {
        if (Object is not { } obj) return;
        Dictionary<string, object> settings = Effective(obj);
        RestartClock(settings);
        Broadcast(obj.Id, settings);
    }

    int RemoveAll()
    {
        List<WorldObject> themes = _session.World.FindAll(WorldObjectType.Theme);
        foreach (WorldObject old in themes)
        {
            List<(int Id, bool ObjectLink)> links = _session.World.LinksAround(old.Id);
            List<int> prototypes = _session.World.RemoveTree(old.Id);
            UnregisterWorldObject.Broadcast(_session, old.Id, prototypes, null, links);
        }
        if (themes.Count > 0)
        {
            _session.World.MarkChanged();
            _skybox.Update(null, 0f);
        }
        return themes.Count;
    }

    void Store(int id, Dictionary<string, object> settings)
    {
        List<(string Key, PackedType Type, object Value)> packed = PackedData.FromPhoton(ThemeValues.ToTable(settings));
        _session.World.Modify(id, obj =>
        {
            obj.Data.RemoveAll(pair => pair.Key == SettingsKey);
            obj.Data.Add((SettingsKey, PackedType.Hashtable, packed));
        });
        _session.World.MarkChanged();
    }

    void Broadcast(int id, Dictionary<string, object> settings) => Send(id, settings, _session.Players);

    void Send(int id, Dictionary<string, object> settings, IReadOnlyList<Player> players)
    {
        float time = SentTime(settings);
        var evt = new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.WorldObjectData] = new Dictionary<object, object?> { [SettingsKey] = Untyped(WireTable(settings, time)) },
            },
        };
        Snapshot subtree = _session.World.SubtreeSnapshot(id);
        List<Player> audience = [.. players.Where(player => player.InWorld && player.Knows(subtree))];
        Remember(audience, time);
        foreach (Player player in audience)
            player.Peer.Send(evt);
        _skybox.Update(settings, Time);
    }

    void Remember(IEnumerable<Player> players, float time)
    {
        lock (_sync)
            foreach (Player player in players)
                _sentTime[player.Actor] = time;
    }

    void Retime(Dictionary<string, object> before, Dictionary<string, object> after, bool startChanged)
    {
        bool modeChanged = Flag(before, ThemeKeys.DayNightCycle) != Flag(after, ThemeKeys.DayNightCycle)
            || Flag(before, ThemeKeys.ServerTime) != Flag(after, ThemeKeys.ServerTime);
        if (startChanged || modeChanged)
        {
            RestartClock(after);
            return;
        }
        if (Value(before, ThemeKeys.CycleLength) != Value(after, ThemeKeys.CycleLength))
        {
            float now = Clock(before);
            lock (_sync)
            {
                _clockTime = now;
                _clockStart = Environment.TickCount64;
            }
        }
    }

    void RestartClock(Dictionary<string, object>? settings)
    {
        lock (_sync)
        {
            _clockTime = settings is null ? 0f : Wrap(Value(settings, ThemeKeys.StartTime));
            _clockStart = Environment.TickCount64;
        }
    }

    float SentTime(Dictionary<string, object> settings) =>
        Flag(settings, ThemeKeys.DayNightCycle) && !Flag(settings, ThemeKeys.ServerTime)
            ? Clock(settings)
            : Value(settings, ThemeKeys.StartTime);

    float Clock(Dictionary<string, object> settings)
    {
        float length = Math.Max(4f, Value(settings, ThemeKeys.CycleLength));
        lock (_sync)
            return Wrap(_clockTime + (Environment.TickCount64 - _clockStart) / 1000f * 100f / length);
    }

    Dictionary<string, object> Effective(WorldObject obj)
    {
        ThemeEntry? theme = Themes.Find(Identifier(obj)) ?? Themes.Find(Text(obj, BaseKey));
        var settings = new Dictionary<string, object>(theme?.Defaults ?? new Dictionary<string, object>());
        foreach ((string key, object value) in ThemeValues.Read(obj.Data.Find(pair => pair.Key == SettingsKey).Value))
            settings[key] = value;
        return settings;
    }

    static Dictionary<string, object> Merge(IReadOnlyDictionary<string, object> current, IReadOnlyDictionary<string, object> changes)
    {
        var merged = new Dictionary<string, object>(current);
        foreach ((string key, object value) in changes)
            merged[key] = value;
        return merged;
    }

    static WorldObject Wire(WorldObject obj, Dictionary<string, object> settings, float time, bool custom)
    {
        string? identifier = Identifier(obj);
        string? fallback = custom || Themes.Find(identifier) is { Custom: false } ? null : Themes.Find(identifier)?.Base ?? Text(obj, BaseKey);
        WorldObject wired = Copy(obj, settings, time);
        if (fallback is not null)
            wired.Data = [.. wired.Data.Where(pair => pair.Key != IdentifierKey), (IdentifierKey, PackedType.String, fallback)];
        return wired;
    }

    static WorldObject Copy(WorldObject obj, Dictionary<string, object> settings, float time) => new()
    {
        Id = obj.Id,
        ParentId = obj.ParentId,
        ItemId = obj.ItemId,
        Type = obj.Type,
        Position = obj.Position,
        Rotation = obj.Rotation,
        Scale = obj.Scale,
        Data = [.. obj.Data.Where(pair => pair.Key != SettingsKey), (SettingsKey, PackedType.Hashtable, PackedData.FromPhoton(WireTable(settings, time)))],
        Owner = obj.Owner,
        PreviewOwner = obj.PreviewOwner,
        Runtime = obj.Runtime,
        Transient = obj.Transient,
    };

    static Dictionary<object, object?> WireTable(Dictionary<string, object> settings, float time) =>
        ThemeValues.ToTable(new Dictionary<string, object>(settings) { [ThemeKeys.StartTime] = time });

    static PhotonDictionary Untyped(Dictionary<object, object?> table)
    {
        PhotonDictionary dictionary = PhotonDictionary.Untyped();
        foreach ((object key, object? value) in table)
            dictionary.Add(key, value is Dictionary<object, object?> nested ? Untyped(nested) : value);
        return dictionary;
    }

    static string? Identifier(WorldObject obj) => Text(obj, IdentifierKey);

    static string? Text(WorldObject obj, string key) => obj.Data.Find(pair => pair.Key == key).Value as string;

    static bool Flag(Dictionary<string, object> settings, string key) => settings.GetValueOrDefault(key) is true;

    static float Value(Dictionary<string, object> settings, string key) => settings.GetValueOrDefault(key) as float? ?? 0f;

    static float Wrap(float time) => (time % 100f + 100f) % 100f;
}
