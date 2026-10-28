using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Content;
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
    public long? AccountId => Stores.Profiles.Account(player.ProfileId);
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

    public bool SendScriptMessage(string name, string data) => ScriptMessages.Send(player.Token, name, data);

    public void Say(string text) => ServerChat.Say(session, player, text);

    public void Notify(string text, bool brief = false) => ServerChat.Notify(player, text, brief);

    public void Announce(string text, string? icon = null) => session.Announce(text, icon, [player]);

    public void Kick() => player.Peer.Disconnect();

    public Api.Team Team => (Api.Team)(int)player.Team;

    public bool SetTeam(Api.Team team) => player.Team == (Game.Team)(int)team || session.Teams.Set(player, (Game.Team)(int)team);

    public bool PressLocally(int id) => session.Logic.PressFor(player, id);

    public ICubeEdit EditTerrainLocally() =>
        session.World.FindFirst(WorldObjectType.CubeModelPrototypeTerrain)?.PrototypeId is int id && session.World.FindPrototype(id) is { } terrain
            ? new CubeEdit(session.World, session, terrain, player)
            : throw new InvalidOperationException("this world has no terrain");

    public bool SetDataLocally(int id, IReadOnlyDictionary<string, object> values)
    {
        if (!player.InWorld || session.World.Find(id) is null || !player.Knows(session.World.SubtreeSnapshot(id))) return false;
        player.Peer.Send(new Photon.EventData((byte)Kogama.EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)Kogama.ParameterKey.WorldObjectID] = id,
                [(byte)Kogama.ParameterKey.WorldObjectData] = values.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value),
            },
        });
        return true;
    }

    public bool Equip(AvatarItem item)
    {
        if (!Playing || item is AvatarItem.Unknown) return false;
        Pickup.Give(session, player, (Kogama.AvatarItemType)(int)item);
        return true;
    }

    public bool Equip(AvatarItem item, Api.Material material)
    {
        if (!Playing || item is AvatarItem.Unknown) return false;
        Pickup.Give(session, player, (Kogama.AvatarItemType)(int)item, (byte)material);
        return true;
    }

    public bool GiveLightningSpeed() => Playing && Boosts.LightningSpeed(session, player);

    public bool Teleport(Vector3 position, TeleportMethod method) => method switch
    {
        TeleportMethod.HiddenVehicle => Free && Teleports.HiddenVehicle(session, player, position),
        TeleportMethod.Teleporter => Playing && Teleports.Teleporter(session, player, position),
        TeleportMethod.Direct => (Playing || Building) && Teleports.Direct(player, position),
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

    bool Building => player.InWorld && player.BuildAvatarId >= 0 && player.ActiveSpawnRole == player.BuildAvatarId;

    bool Free => Playing && session.World.Find(player.PlayAvatar) is { } avatar && (Runtime(avatar, "seat") as int? ?? -1) < 0;

    public bool IsAdmin => PluginHost.Admin(player);

    public bool Alive =>
        player.InWorld && session.World.Find(player.PlayAvatar) is { } avatar
        && ((Runtime(avatar, "avatarModeTypes") as int? ?? PlayingMode) & PlayingMode) != 0;

    bool Playing =>
        Alive && player.ActiveSpawnRole == player.PlayAvatar
        && !session.World.ToSnapshot().Objects.Any(obj => obj.Type == WorldObjectType.GodzillaTrigger && Colossus.Occupant(obj) == player.PlayAvatar);

    static object? Runtime(WorldObject obj, string key) => obj.Runtime.Find(pair => pair.Key == key).Value;

    public void Damage(float amount, INpc? by = null, Vector3? impulse = null)
    {
        Npc? attacker = by is null ? null : session.NpcByActor(by.Id);
        if (attacker?.Team == player.Team) return;
        session.DamagePlayer(player, by?.Id ?? player.Actor, amount, impulse ?? Vector3.Zero, by is null ? KilledBy.Environmental
            : attacker?.ColossusScale is not null ? KilledBy.GodzillaLaser : KilledBy.Mutant);
        if (attacker is not null) PluginHost.DamagedByNpc(session, player, attacker, amount);
    }
}
