using System.Numerics;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.World;
using OpenKogama.Kogama;

namespace OpenKogama.Plugins;

sealed class PluginNpc(Session session, Npc npc) : INpc
{
    public int Id => npc.Actor;
    public int ObjectId => npc.AvatarId;
    public string Name => npc.Name;
    public IWorld World => PluginHost.WorldOf(session);
    public Api.Team? Team => npc.Team is Game.Team team ? (Api.Team)(int)team : null;

    public AvatarSkin Skin => new(
        [.. (npc.Parts ?? []).Select(part => new AvatarSkinPart(part.Bone, part.Scale, part.Cubes))],
        [.. (npc.Accessories ?? []).Select(worn => new AvatarSkinAccessory(worn.Item, worn.Slot, worn.Offset, worn.Scale))]);
    public Vector3 Position => new(npc.Position[0], npc.Position[1], npc.Position[2]);
    public Vector3 EyePosition => Position + Vector3.UnitY * npc.EyeHeight;
    public float Yaw => npc.Yaw;
    public string Animation => npc.Animation;
    public bool Walking => npc.Target is not null;
    public bool Grounded => npc.Grounded;
    public float Health => npc.Health;
    public bool Alive => npc.Alive;
    public bool Exists => session.Npcs.Contains(npc);

    public float Speed
    {
        get => npc.Speed;
        set => npc.Speed = Math.Max(0f, value);
    }

    public float Size => npc.Size;
    public int Level => npc.Level;

    public bool OnFire
    {
        get => npc.OnFire;
        set => npc.OnFire = value;
    }

    public string? Holding
    {
        get => npc.HeldItem is int item ? ((AvatarItemType)item).ToString() : null;
        set
        {
            npc.HeldItem = value is null ? null
                : Enum.TryParse(value, ignoreCase: true, out AvatarItemType item) ? (int)item
                : throw new ArgumentException($"unknown item {value}, use one of {string.Join(", ", Enum.GetNames<AvatarItemType>())}");
            npc.HeldItemChanged = true;
        }
    }

    public void Shoot(Vector3 direction) => Shoot(direction, 0f);

    public void Shoot(Vector3 direction, float holdSeconds) =>
        npc.Shots.Enqueue((Vector3.Normalize(direction), (int)(Math.Max(0f, holdSeconds) * 1000f)));

    public void Aim(Vector3 direction) => npc.Aims.Enqueue(Vector3.Normalize(direction));

    public bool Hold(IWorldObject weapon)
    {
        WorldObject? pickup = session.World.Find(weapon.Id);
        if (pickup?.Type == WorldObjectType.BlueprintCustomGun)
            pickup = session.World.Subtree(pickup.Id).Find(obj => obj.Type == WorldObjectType.PickupCustomGun);
        if (pickup?.Data.Find(pair => pair.Key == "itemType").Value is not int item) return false;

        npc.HeldItem = item;
        npc.HeldItemData = pickup.Data.Find(pair => pair.Key == "itemData").Value is List<(string Key, PackedType Type, object Value)> data
            ? (Dictionary<object, object?>)PackedData.ToPhoton(data)
            : null;
        npc.HeldItemChanged = true;
        return true;
    }

    public bool BecomeColossus(IWorldObject trigger) =>
        session.World.Find(trigger.Id) is { Type: WorldObjectType.GodzillaTrigger } target && session.MakeColossus(npc, target);

    public bool BecomeColossus(ColossusSize size) => session.MakeColossus(npc, Colossus.PackageOf((int)size));

    public float MaxHealth
    {
        get => npc.MaxHealth;
        set
        {
            float max = Math.Max(1f, value);
            npc.Health = npc.Health * max / npc.MaxHealth;
            npc.MaxHealth = max;
            npc.HealthChanged = npc.MaxHealthChanged = true;
        }
    }

    public void WalkTo(Vector3 target) => npc.Target = [target.X, target.Y, target.Z];

    public void Stop() => npc.Target = null;

    public void Jump(float holdSeconds = 0.5f) => npc.JumpUntil = Environment.TickCount64 + (long)(Math.Max(0f, holdSeconds) * 1000f);

    public void Teleport(Vector3 position, float? yaw = null)
    {
        npc.Target = null;
        npc.Position = [position.X, position.Y, position.Z];
        if (yaw is float facing) npc.Yaw = facing;
        npc.Teleported = true;
    }

    public void Face(float yaw) => npc.Yaw = yaw;

    public void PlayAnimation(string? state) => npc.AnimationOverride = state;

    public void Damage(float amount, IPlayer? by = null) => Damage(amount, by, Vector3.Zero);

    public void Damage(float amount, IPlayer? by, Vector3 impulse) => npc.Damages.Enqueue((amount, by?.Actor ?? npc.Actor, impulse));

    public void Damage(float amount, INpc by) => Damage(amount, by, Vector3.Zero);

    public void Damage(float amount, INpc by, Vector3 impulse) => npc.Damages.Enqueue((amount, by.Id, impulse));

    public bool SetTeam(Api.Team? team) => session.Teams.Set(npc, team is Api.Team joined ? (Game.Team)(int)joined : null);

    public void Say(string text) => ServerChat.Say(session, npc, text);

    public void Respawn(Vector3 position) => npc.Respawns.Enqueue(position);

    public void Remove() => session.RemoveNpc(npc);
}
