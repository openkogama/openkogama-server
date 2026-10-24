using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Handlers.Operations;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class PluginPlayer(Player player, Session session) : IPlayer
{
    const byte DriverSeat = 0;
    const int PlayingMode = 1;

    public int Actor => player.Actor;
    public int ProfileId => player.ProfileId;
    public string Name => player.Username;
    public string ClientVersion => player.Build;
    public IWorld World => PluginHost.WorldOf(session);
    public int Xp => Stores.Profiles.Xp(player.ProfileId);
    public int Gold => Stores.Profiles.Gold(player.ProfileId);
    public AvatarSkin Skin => AvatarFiles.Export(Stores.Profiles.ActiveAvatar(player.ProfileId)) ?? new AvatarSkin([], []);

    public Vector3? Position => player.LastPosition is float[] position ? new Vector3(position[0], position[1], position[2]) : null;

    public int? SoundEmitter => player.SoundEmitter;

    public AvatarItem Holding =>
        session.World.Find(player.PlayAvatar)?.Runtime.Find(pair => pair.Key == "currentItem").Value is List<(string Key, PackedType Type, object Value)> item
        && item.Find(pair => pair.Key == "type").Value is int type
            ? PluginHost.ItemOf(type)
            : AvatarItem.Hand;

    public int AddXp(int amount) => Experience.Award(session, player, amount, Experience.NoReason);

    public int AddGold(int amount) => Stores.Profiles.AddGold(player.ProfileId, amount);

    public void Message(string text, string? color = null) => ServerChat.Message(player, text, color);

    public void Say(string text) => ServerChat.Say(session, player, text);

    public void Notify(string text, bool brief = false) => ServerChat.Notify(player, text, brief);

    public void Announce(string text, string? icon = null) => session.Announce(text, icon, [player]);

    public void Kick() => player.Peer.Disconnect();

    public Api.Team Team => (Api.Team)(int)player.Team;

    public bool SetTeam(Api.Team team) => player.Team == (Game.Team)(int)team || session.Teams.Set(player, (Game.Team)(int)team);

    public bool PressLocally(int id) => session.Logic.PressFor(player, id);

    public bool Equip(AvatarItem item)
    {
        if (!Playing || item is AvatarItem.Unknown) return false;
        Pickup.Give(session, player, (Kogama.AvatarItemType)(int)item);
        return true;
    }

    public bool GiveLightningSpeed() => Playing && Boosts.LightningSpeed(session, player);

    public bool Teleport(Vector3 position, TeleportMethod method) => method switch
    {
        TeleportMethod.HiddenVehicle => Free && Teleports.HiddenVehicle(session, player, position),
        TeleportMethod.Teleporter => Playing && Teleports.Teleporter(session, player, position),
        _ => false,
    };

    public bool EnterVehicle(IWorldObject vehicle)
    {
        if (!Free || session.World.Find(vehicle.Id) is not { } target || !player.Knows(session.World.SubtreeSnapshot(target.Id))) return false;
        if (target.Type == WorldObjectType.WorldObjectSpawnerVehicle)
            return SpawnVehicleWithDriver.Spawn(session, player, player.PlayAvatar, target, DriverSeat, player.Peer.ServerTime) is not null;
        if (!Vehicles.Types.Contains(target.Type) || session.World.Find(target.ParentId)?.Type == WorldObjectType.WorldObjectSpawnerVehicle) return false;
        return AttachWorldObjectToSeat.Seat(session, player, player.PlayAvatar, target.Id, DriverSeat);
    }

    public bool BecomeColossus(IWorldObject trigger) =>
        Free && Colossus.Supported(player)
        && session.World.Find(trigger.Id) is { Type: WorldObjectType.GodzillaTrigger } target
        && Colossus.Enter(session, target, player.PlayAvatar);

    public int ReauthsLeft => Reauths.Left(player);

    public string Language => player.Language ?? "en";

    public bool Reauth(ReauthOptions options) =>
        (options.World is not int world || Stores.Worlds.World(world) is not null)
        && Reauths.Start(player, new Reauths.Plan(options.World, ModeOf(options.Mode), new Dictionary<string, object?>(options.Session)));

    public bool MoveToWorld(int world, WorldMode mode = WorldMode.Play) => Reauth(new ReauthOptions { World = world, Mode = mode });

    static GameMode? ModeOf(WorldMode? mode) => mode switch
    {
        WorldMode.Play => GameMode.Play,
        WorldMode.Build => GameMode.Edit,
        WorldMode.AvatarEditor => GameMode.CharacterEditor,
        _ => null,
    };

    bool Free => Playing && session.World.Find(player.PlayAvatar) is { } avatar && (Runtime(avatar, "seat") as int? ?? -1) < 0;

    public bool IsAdmin => PluginHost.Admin(player);

    public bool Alive =>
        player.InWorld && session.World.Find(player.PlayAvatar) is { } avatar
        && ((Runtime(avatar, "avatarModeTypes") as int? ?? PlayingMode) & PlayingMode) != 0;

    bool Playing =>
        Alive && player.ActiveSpawnRole == player.PlayAvatar
        && !session.World.ToSnapshot().Objects.Any(obj => obj.Type == WorldObjectType.GodzillaTrigger && Colossus.Occupant(obj) == player.PlayAvatar);

    static object? Runtime(WorldObject obj, string key) => obj.Runtime.Find(pair => pair.Key == key).Value;

    public void Damage(float amount, INpc? by = null, Vector3? impulse = null) =>
        session.DamagePlayer(player, by?.Id ?? player.Actor, amount, impulse ?? Vector3.Zero, by is null ? KilledBy.Environmental
            : session.Npcs.FirstOrDefault(npc => npc.Actor == by.Id)?.ColossusScale is not null ? KilledBy.GodzillaLaser : KilledBy.Mutant);
}
