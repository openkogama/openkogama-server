using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Themes
{
    static readonly List<string> Identifiers = JsonSerializer.Deserialize<List<string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "themes.json"))) ?? [];

    public static string Catalog() => JsonSerializer.Serialize(Identifiers.Select((identifier, index) => new
    {
        id = index + 1,
        themeIdentifier = identifier,
        priceGold = 0,
        levelRequirement = 0,
    }));

    public static bool Apply(Session session, int themeId, Dictionary<object, object?> settings)
    {
        if (themeId < 1 || themeId > Identifiers.Count) return false;

        foreach (WorldObject old in session.World.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.Theme).ToList())
        {
            session.World.RemoveTree(old.Id);
            var gone = new EventData((byte)EventCode.UnregisterWorldObject)
            {
                Parameters = { [(byte)ParameterKey.WorldObjectID] = old.Id },
            };
            foreach (Player player in session.Players)
                player.Peer.Send(gone);
        }

        var theme = new WorldObject
        {
            Id = 1,
            ParentId = -1,
            Type = WorldObjectType.Theme,
            Data =
            [
                ("identifier", PackedType.String, Identifiers[themeId - 1]),
                ("settings", PackedType.Hashtable, PackedData.FromPhoton(settings)),
            ],
        };
        Snapshot added = session.World.Insert(new Snapshot([], [theme], [], []), session.World.RootId);
        session.World.MarkChanged();
        Handlers.Operations.GetNextGameBatch.SendAdded(session, 0, added);
        Console.WriteLine($"world {session.WorldId}: theme {Identifiers[themeId - 1]}");
        return true;
    }
}
