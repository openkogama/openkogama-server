using System.Globalization;
using System.Text.Json;
using OpenKogama.Api;

namespace Themes;

public sealed class ThemesPlugin : IPlugin
{
    const string Folder = "plugins/data/themes";
    const string ReplaceFolder = "Replace";
    const string Settings = "theme.json";
    static readonly string[] Formats = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif"];

    sealed record Info(string? Base, string? Title, string? Description);

    static readonly ThemeSettings RealSky = new()
    {
        DayNightCycle = true,
        ServerTime = false,
        Palette = ThemePalette.Default,
        StartTime = 35f,
        CycleLength = 1200f,
        TopColor = ThemeColor.FromHex("#2f6fc4"),
        BottomColor = ThemeColor.FromHex("#cfe3f3"),
        SunTint = ThemeColor.FromHex("#fff1d6"),
        MoonTint = ThemeColor.FromHex("#f4f6fb"),
        SunHeight = 0.22f,
        SunAxis = 180f,
        SunSize = 1f,
        MoonSize = 0.6f,
        MoonHeight = 0.4f,
        MoonAxis = 0f,
        StarsTwinklingSpeed = 2f,
        CloudsHeight = -0.15f,
        CloudsOffset = 0f,
        CloudsRotationSpeed = 0.5f,
        Fog = false,
    };

    static readonly ThemeCycle DayAndNight = new()
    {
        Sunrise = 25f,
        Sunset = 75f,
        Moonrise = 76f,
        Moonset = 24f,
        Sky =
        [
            SkyAt(0f, "#08122a", "#1a3560"),
            SkyAt(12f, "#08122a", "#1a3560"),
            SkyAt(17.5f, "#0b1631", "#233c66"),
            SkyAt(20f, "#142650", "#4a5a85"),
            SkyAt(22.5f, "#1f3c72", "#c98f86"),
            SkyAt(25f, "#3a62a3", "#f6a26e"),
            SkyAt(27.5f, "#4d7fc4", "#f7cf98"),
            SkyAt(31f, "#3d78cc", "#c4ddef"),
            SkyAt(38f, "#2f6fc4", "#cfe3f3"),
            SkyAt(50f, "#2a68c2", "#d6e8f6"),
            SkyAt(62f, "#2f6fc4", "#cfe3f3"),
            SkyAt(69f, "#3a72c4", "#dfe2d2"),
            SkyAt(72.5f, "#4a73b5", "#f4c389"),
            SkyAt(75f, "#3c5596", "#f2804a"),
            SkyAt(76.5f, "#33477f", "#dc6a5e"),
            SkyAt(78f, "#263a73", "#a05a84"),
            SkyAt(80f, "#1a2c5f", "#4f4a80"),
            SkyAt(82f, "#11214a", "#2e4170"),
            SkyAt(84.5f, "#0a1631", "#1d3560"),
            SkyAt(88f, "#08122a", "#1a3560"),
        ],
        Sun =
        [
            LightAt(19f, "#ff6a2a00", "#ff8a50", 0f),
            LightAt(23.5f, "#ff6a2a66", "#ff8a50", 0.08f),
            LightAt(25f, "#ff7a38", "#ff9d5c", 0.3f),
            LightAt(27.5f, "#ffaa5c", "#ffc489", 0.65f),
            LightAt(31f, "#ffdcaa", "#ffe7c8", 0.9f),
            LightAt(38f, "#fff1d6", "#fff5e8", 1f),
            LightAt(50f, "#fff6e8", "#fffaf2", 1.05f),
            LightAt(62f, "#fff1d6", "#fff5e8", 1f),
            LightAt(69f, "#ffdcaa", "#ffe3c0", 0.9f),
            LightAt(72.5f, "#ffa452", "#ffbd7c", 0.65f),
            LightAt(75f, "#ff6a2c", "#ff8a4c", 0.3f),
            LightAt(76.5f, "#ff5a2466", "#ff7448", 0.08f),
            LightAt(81f, "#ff5a2400", "#ff7448", 0f),
        ],
        Moon =
        [
            LightAt(74f, "#e8eef800", "#8ea6d8", 0f),
            LightAt(78f, "#f0f3fa", "#9fb4e2", 0.2f),
            LightAt(84f, "#f4f6fb", "#a9bce6", 0.45f),
            LightAt(0f, "#f6f8fc", "#b0c2ea", 0.5f),
            LightAt(16f, "#f4f6fb", "#a9bce6", 0.45f),
            LightAt(22f, "#f0f3fa", "#9fb4e2", 0.2f),
            LightAt(26f, "#e8eef800", "#8ea6d8", 0f),
        ],
        Stars =
        [
            StarsAt(76f, "#c8d4ff00"),
            StarsAt(80f, "#c8d4ff80"),
            StarsAt(84f, "#d8e2ff"),
            StarsAt(0f, "#e2eaff"),
            StarsAt(17f, "#d8e2ff"),
            StarsAt(20.5f, "#c8d4ff80"),
            StarsAt(24f, "#c8d4ff00"),
        ],
    };

    public void Load(IServer server)
    {
        server.Assets.Themes.Add(new CustomTheme
        {
            Name = "Real Sky",
            Base = GameTheme.Normal,
            Title = "Real Sky",
            Description = "A real day and night with sunsets and stars",
            Defaults = RealSky,
            Textures = Embedded(),
            Cycle = DayAndNight,
        });

        Directory.CreateDirectory(Path.Combine(Folder, ReplaceFolder));
        foreach (string folder in Directory.EnumerateDirectories(Folder))
        {
            string name = Path.GetFileName(folder);
            try
            {
                if (name.Equals(ReplaceFolder, StringComparison.OrdinalIgnoreCase)) ReplaceAll(server, folder);
                else Add(server, folder, name);
            }
            catch (Exception error)
            {
                server.Log($"themes: {name} skipped, {error.Message}");
            }
        }
        server.Log($"themes: {server.Assets.Themes.All.Count(theme => theme.Custom)} of our own from {Folder}");
        server.AddCommand("theme", (player, arguments) => Theme(server, player, arguments));
        server.AddCommand("sky", (player, arguments) => Sky(player, arguments));
    }

    static ThemeSkyKey SkyAt(float time, string top, string bottom) => new(time, ThemeColor.FromHex(top), ThemeColor.FromHex(bottom));

    static ThemeLightKey LightAt(float time, string tint, string light, float intensity) =>
        new(time, ThemeColor.FromHex(tint), ThemeColor.FromHex(light), intensity);

    static ThemeStarsKey StarsAt(float time, string tint) => new(time, ThemeColor.FromHex(tint));

    static Dictionary<ThemeTexture, byte[]> Embedded()
    {
        var pictures = new Dictionary<ThemeTexture, byte[]>();
        foreach (ThemeTexture texture in Enum.GetValues<ThemeTexture>())
        {
            using Stream? stream = typeof(ThemesPlugin).Assembly.GetManifestResourceStream($"sky/{texture.ToString().ToLowerInvariant()}.png");
            if (stream is null) continue;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            pictures[texture] = copy.ToArray();
        }
        return pictures;
    }

    static void Add(IServer server, string folder, string name)
    {
        string settings = Path.Combine(folder, Settings);
        Info info = File.Exists(settings) ? JsonSerializer.Deserialize<Info>(File.ReadAllText(settings), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(null, null, null) : new(null, null, null);
        server.Assets.Themes.Add(new CustomTheme
        {
            Name = name,
            Base = TryName(info.Base ?? "", out GameTheme basis) ? basis : GameTheme.Normal,
            Title = info.Title,
            Description = info.Description,
            Textures = Pictures(folder),
        });
    }

    static void ReplaceAll(IServer server, string folder)
    {
        foreach (string themeFolder in Directory.EnumerateDirectories(folder))
        {
            string name = Path.GetFileName(themeFolder);
            if (!TryName(name, out GameTheme theme)) throw new ArgumentException($"{name} is no game theme, name the folder like Normal or Christmas");
            foreach ((ThemeTexture texture, byte[] image) in Pictures(themeFolder))
                server.Assets.Themes.Replace(theme, texture, image);
        }
    }

    static Dictionary<ThemeTexture, byte[]> Pictures(string folder)
    {
        var pictures = new Dictionary<ThemeTexture, byte[]>();
        foreach (string file in Directory.EnumerateFiles(folder).Where(file => Formats.Contains(Path.GetExtension(file).ToLowerInvariant())))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!TryName(name, out ThemeTexture texture)) throw new ArgumentException($"{Path.GetFileName(file)} is no theme texture, name it like clouds, stars, sun, moon, sunflare, moonflare or preview");
            pictures[texture] = File.ReadAllBytes(file);
        }
        return pictures;
    }

    static bool TryName<T>(string text, out T value) where T : struct, Enum
    {
        string? name = Enum.GetNames<T>().FirstOrDefault(candidate => candidate.Equals(text, StringComparison.OrdinalIgnoreCase));
        value = name is null ? default : Enum.Parse<T>(name);
        return name is not null;
    }

    static void Theme(IServer server, IPlayer player, string[] arguments)
    {
        IWorldTheme sky = player.World.Theme;
        if (arguments.Length == 0)
        {
            string current = sky.Current is { } theme ? $"{theme.Title} ({theme.Name})" : "none";
            player.Message($"Theme: {current}\n{string.Join(", ", server.Assets.Themes.All.Select(theme => theme.Name))}\n!theme <name> or !theme off");
            return;
        }
        if (!player.IsAdmin)
        {
            player.Message("Only admins change the theme");
            return;
        }

        string wanted = string.Join(' ', arguments);
        if (wanted.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            player.Message(sky.Remove() ? "Theme removed" : "This world has no theme");
            return;
        }
        if (server.Assets.Themes.All.FirstOrDefault(theme => theme.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
        {
            player.Message($"No theme called {wanted}, type !theme for the list");
            return;
        }
        sky.Set(chosen);
        player.Message($"Theme {chosen.Title}");
    }

    static void Sky(IPlayer player, string[] arguments)
    {
        IWorldTheme sky = player.World.Theme;
        if (arguments.Length == 0 || sky.Current is null)
        {
            player.Message(sky.Current is null ? "Put a theme on first, !theme <name>" : $"Time {sky.Time:0}. !sky time <0-100>, cycle <seconds|off>, fog <density|off>, color <#top> <#bottom>, sun <-1 to 1>, exposure <0-3>");
            return;
        }
        if (!player.IsAdmin)
        {
            player.Message("Only admins change the sky");
            return;
        }

        string value = arguments.ElementAtOrDefault(1) ?? "";
        bool off = value.Equals("off", StringComparison.OrdinalIgnoreCase);
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number);
        switch (arguments[0].ToLowerInvariant())
        {
            case "time":
                sky.Time = number;
                break;
            case "cycle":
                sky.Change(off ? new ThemeSettings { DayNightCycle = false } : new ThemeSettings { DayNightCycle = true, ServerTime = false, CycleLength = number });
                break;
            case "fog":
                sky.Change(off ? new ThemeSettings { Fog = false } : new ThemeSettings { Fog = true, FogDensity = number });
                break;
            case "color" when arguments.Length >= 3:
                sky.Change(new ThemeSettings { DayNightCycle = false, TopColor = ThemeColor.FromHex(arguments[1]), BottomColor = ThemeColor.FromHex(arguments[2]) });
                break;
            case "sun":
                sky.Change(new ThemeSettings { DayNightCycle = false, SunHeight = number });
                break;
            case "exposure":
                sky.Change(new ThemeSettings { Exposure = number });
                break;
            default:
                player.Message("Unknown sky setting, type !sky");
                return;
        }
        player.Message("Sky changed");
    }
}
