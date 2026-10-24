using System.Globalization;
using System.Numerics;
using OpenKogama.Api;

namespace Portals;

public sealed class PortalsPlugin : IPlugin
{
    const float NearDistance = 8f;
    const string Info = "#FFA500";
    const string Error = "#FF5555";
    const string Usage = "Use !portal <object id> <world id>, !portal remove <object id>, !portal list or !portal near";
    static readonly HashSet<string> Kinds = ["PressurePlate", "TriggerBox", "TriggerCube", "ShootableButton", "UseLever", "Teleporter"];

    readonly HashSet<IPlayer> _leaving = [];
    IPluginStorage _links = null!;

    public void Load(IServer server)
    {
        _links = server.Storage("portals");
        server.AddCommand("portal", Command);
        server.PlayerTriggered += Triggered;
        server.PlayerLeft += player =>
        {
            lock (_leaving) _leaving.Remove(player);
        };
    }

    void Triggered(IPlayer player, IWorldObject obj, bool on)
    {
        IWorld world = player.World;
        if (!on || Target(world.Id, obj.Id) is not int target || target == world.Id) return;
        lock (_leaving)
            if (_leaving.Contains(player)) return;

        if (player.MoveToWorld(target, world.Play ? WorldMode.Play : WorldMode.Build))
        {
            lock (_leaving) _leaving.Add(player);
            return;
        }
        player.Message(player.ReauthsLeft == 0
            ? "Your game can't switch worlds, turn on World switching in the launcher's client patches or restart the game"
            : $"World {target} can't be reached", Error);
    }

    void Command(IPlayer player, string[] args)
    {
        if (!player.IsAdmin)
        {
            player.Message("Only the server's admins can set portals", Error);
            return;
        }

        IWorld world = player.World;
        switch (args)
        {
            case ["list"]:
                List(player, world);
                break;
            case ["near"]:
                Near(player, world);
                break;
            case ["remove", var id] when Number(id) is int objectId:
                bool known = Target(world.Id, objectId) is not null;
                _links.Set(Key(world.Id, objectId), null);
                player.Message(known ? $"Object {objectId} no longer leads anywhere" : $"Object {objectId} wasn't a portal", known ? Info : Error);
                break;
            case [var id, var to] when Number(id) is int objectId && Number(to) is int target:
                Link(player, world, objectId, target);
                break;
            default:
                player.Message(Usage, Error);
                break;
        }
    }

    void Link(IPlayer player, IWorld world, int objectId, int target)
    {
        if (world.Find(objectId) is not { } obj)
        {
            player.Message($"This world has no object {objectId}", Error);
            return;
        }
        if (!Kinds.Contains(obj.Type))
        {
            player.Message($"{obj.Type} can't be a portal, use a Pressure Plate, Trigger Box, Trigger Cube, Button, Lever or Teleporter", Error);
            return;
        }
        if (target == world.Id)
        {
            player.Message("A portal has to lead to another world", Error);
            return;
        }
        _links.Set(Key(world.Id, objectId), target.ToString(CultureInfo.InvariantCulture));
        player.Message($"{obj.Type} {objectId} now leads to world {target}", Info);
    }

    void List(IPlayer player, IWorld world)
    {
        string prefix = world.Id.ToString(CultureInfo.InvariantCulture) + ":";
        List<string> lines = [.. _links.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => $"{key[prefix.Length..]} -> world {_links.Get(key)}")];
        player.Message(lines.Count == 0 ? "No portals in this world" : "Portals: " + string.Join(", ", lines), Info);
    }

    void Near(IPlayer player, IWorld world)
    {
        if (player.Position is not Vector3 here)
        {
            player.Message("Your position isn't known yet, move a bit and try again", Error);
            return;
        }
        List<string> found = [.. world.Objects
            .Where(obj => Kinds.Contains(obj.Type))
            .Select(obj => (obj, distance: Vector3.Distance(here, WorldPosition(world, obj))))
            .Where(entry => entry.distance <= NearDistance)
            .OrderBy(entry => entry.distance)
            .Select(entry => $"{entry.obj.Type} {entry.obj.Id}" + (Target(world.Id, entry.obj.Id) is int target ? $" (world {target})" : ""))];
        player.Message(found.Count == 0 ? "Nothing that can be a portal is near you" : string.Join(", ", found), Info);
    }

    static Vector3 WorldPosition(IWorld world, IWorldObject obj)
    {
        Matrix4x4 matrix = Matrix4x4.Identity;
        for (IWorldObject? part = obj; part is not null; part = world.Find(part.ParentId))
            matrix *= Matrix4x4.CreateScale(part.Scale) * Matrix4x4.CreateFromQuaternion(part.Rotation) * Matrix4x4.CreateTranslation(part.Position);
        return matrix.Translation;
    }

    int? Target(int world, int objectId) => Number(_links.Get(Key(world, objectId)));

    static string Key(int world, int objectId) => string.Create(CultureInfo.InvariantCulture, $"{world}:{objectId}");

    static int? Number(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : null;
}
