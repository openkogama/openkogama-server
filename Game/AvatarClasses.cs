using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

using Pairs = List<(string Key, PackedType Type, object Value)>;

public static class AvatarClasses
{
    const string Blueprint = "BlueprintData";
    const string ChildrenMap = "ChildrenMap";
    const string ClientSideType = "ClientSideType";
    const string BodyKey = "bodyId";
    const string DatabaseId = "DBId";
    const string TeamKey = "team";
    const string Skills = "AvatarSettings";
    const int NoOwner = -1;
    const int NoTeam = 5;

    public static void AddPreviews(GameWorld world)
    {
        foreach (WorldObject creator in world.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.AvatarSpawnRoleCreator))
        {
            if (creator.Data.Find(pair => pair.Key == TeamKey).Value is int team && team == NoTeam)
                world.Modify(creator.Id, obj => obj.Data = [.. obj.Data.Where(pair => pair.Key != TeamKey), (TeamKey, PackedType.Int32, (int)Team.Blue)]);
            if (PreviewOf(world, creator.Id) is null) AddPreview(world, creator);
            SyncSkills(world, creator.Id);
        }
    }

    public static void SyncSkills(GameWorld world, int creatorId)
    {
        if (world.Find(creatorId) is not { Type: WorldObjectType.AvatarSpawnRoleCreator } creator || PreviewOf(world, creatorId) is not { } preview) return;
        var skills = creator.Data.Find(pair => pair.Key == Skills);
        world.Modify(preview.Id, obj =>
        {
            obj.Data.RemoveAll(pair => pair.Key == Skills);
            if (skills.Key is not null && skills.Value is Pairs values)
                obj.Data.Add((Skills, PackedType.Hashtable, Copy(values)));
        });
    }

    static Pairs Copy(Pairs values) =>
        [.. values.Select(pair => pair.Value is Pairs nested ? (pair.Key, pair.Type, (object)Copy(nested)) : pair)];

    public static WorldObject? PreviewOf(GameWorld world, int creatorId) =>
        world.Subtree(creatorId).Find(obj => obj.ParentId == creatorId && obj.Type == WorldObjectType.Avatar);

    public static void CreateRole(Session session, Player player, int creatorId)
    {
        GameWorld world = session.World;
        if (world.Find(creatorId) is not { Type: WorldObjectType.AvatarSpawnRoleCreator } creator) return;
        SyncSkills(world, creatorId);
        if (PreviewOf(world, creatorId) is not { } preview || world.CloneTree(preview.Id) is not { } role) return;

        WorldObject? current = world.Find(player.ActiveSpawnRole);
        float[] position = player.LastPosition ?? current?.Position ?? creator.Position;
        float[] rotation = player.LastRotation ?? current?.Rotation ?? creator.Rotation;
        int[] stale = [.. player.ClassAvatars.Values];
        player.ClassAvatars.Clear();

        foreach (WorldObject obj in world.Subtree(role.Id))
            world.Modify(obj.Id, part =>
            {
                part.Owner = player.Actor;
                part.Transient = true;
            });
        world.Modify(role.Id, obj =>
        {
            obj.Position = [.. position];
            obj.Rotation = [.. rotation];
        });
        player.ClassAvatars[creatorId] = role.Id;

        var clone = new EventData((byte)EventCode.CloneWorldObjectTree)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = new[] { preview.Id, role.Id },
                [(byte)ParameterKey.OwnerActorNr] = player.Actor,
                [(byte)ParameterKey.LinkID] = -1,
                [(byte)ParameterKey.ObjectLinkID] = -1,
                [(byte)ParameterKey.CloneToRootGroup] = true,
                [(byte)ParameterKey.PreviewProfileOwnerID] = 0,
            },
        };
        var replicate = new EventData((byte)EventCode.ReplicateSpawnRoleData)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = player.SpawnRoleData(),
            },
        };
        foreach (Player other in session.Players)
        {
            if (!other.SpawnRoles || other != player && !other.Saw(player.Actor)) continue;
            other.Peer.Send(clone);
            if (other != player) other.Peer.Send(replicate);
        }
        session.ActivateRole(player, role.Id, position, rotation);

        foreach (int old in stale)
        {
            world.Remove(old);
            var unregister = new EventData((byte)EventCode.UnregisterWorldObject) { Parameters = { [(byte)ParameterKey.WorldObjectID] = old } };
            foreach (Player other in session.Players)
                if (other.SpawnRoles && (other == player || other.Saw(player.Actor)))
                    other.Peer.Send(unregister);
        }
        Console.WriteLine($"peer {player.Peer.Id}: class {creatorId} as {role.Id}");
    }

    public static byte[] Bodies(Player player)
    {
        var data = new List<byte>();
        foreach (StoredAvatar avatar in Stores.Profiles.Avatars(player.ProfileId))
        {
            var world = new GameWorld();
            List<WorldObject> body = Avatar.BuildBody(world, player.Actor, NoOwner, Avatar.AddPrototypes(world));
            foreach (WorldObject obj in body) world.Add(obj);
            Avatar.Dress(world, body[0].Id, avatar.Id);
            world.Modify(body[0].Id, obj => obj.SetRuntime(DatabaseId, PackedType.Int32, avatar.Id));
            data.AddRange(player.WorldData(world.SubtreeSnapshot(body[0].Id), package: true));
        }
        return [.. data];
    }

    public static void SetBody(Session session, Player player, int creatorId, int avatarId)
    {
        GameWorld world = session.World;
        if (session.Play || world.Find(creatorId) is not { Type: WorldObjectType.AvatarSpawnRoleCreator } creator) return;
        if (!Stores.Profiles.Avatars(player.ProfileId).Exists(avatar => avatar.Id == avatarId)) return;
        if (PreviewOf(world, creatorId) is not { } preview) return;

        int oldBody = BodyOf(creator);
        int oldProto = world.Subtree(preview.Id).Find(obj => obj.ParentId == preview.Id && IsBody(obj))?.Id ?? -1;

        List<WorldObject> parts = Avatar.BuildBody(world, NoOwner, creatorId, session.AvatarPrototypes);
        foreach (WorldObject obj in parts)
        {
            obj.Owner = null;
            world.Add(obj);
        }
        int body = parts[0].Id;
        Avatar.Dress(world, body, avatarId);
        if (world.Find(oldBody) is { } previous)
            world.Modify(body, obj =>
            {
                obj.Position = [.. previous.Position];
                obj.Rotation = [.. previous.Rotation];
            });
        if (world.CloneTree(body) is not { } proto) return;
        world.Modify(proto.Id, obj => obj.ParentId = preview.Id);
        if (oldBody >= 0) world.Remove(oldBody);
        if (oldProto >= 0) world.Remove(oldProto);
        world.Modify(creatorId, obj => obj.Data = WithBody(obj.Data, body));
        world.MarkChanged();

        Snapshot[] added = [world.SubtreeSnapshot(body), world.SubtreeSnapshot(proto.Id)];
        string swap = JsonSerializer.Serialize(new
        {
            deletedBodyWoId = oldBody,
            addedBodyWoId = body,
            deletedProtoBodyWoId = oldProto,
            addedProtoBodyWoId = proto.Id,
            spawnRoleCreatorWoId = creatorId,
        });
        foreach (Player other in session.Players)
        {
            if (!other.SpawnRoles || !other.InWorld) continue;
            foreach (Snapshot snapshot in added)
                other.Peer.Send(new EventData((byte)EventCode.GetGameBatch)
                {
                    Parameters =
                    {
                        [(byte)ParameterKey.ActorNr] = player.Actor,
                        [(byte)ParameterKey.Data] = other.WorldData(snapshot),
                        [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
                        [(byte)ParameterKey.QueryId] = Handlers.Operations.GetNextGameBatch.NextQueryId(),
                        [(byte)ParameterKey.QueryDataLeft] = false,
                    },
                });
            other.Peer.Send(new EventData((byte)EventCode.SetSpawnRoleBody) { Parameters = { [(byte)ParameterKey.Data] = swap } });
        }
        Console.WriteLine($"peer {player.Peer.Id}: class {creatorId} body from avatar {avatarId}");
    }

    static void AddPreview(GameWorld world, WorldObject creator)
    {
        var preview = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = creator.Id,
            Type = WorldObjectType.Avatar,
            Owner = NoOwner,
            Transient = true,
            Runtime = Avatar.Runtime(),
        };
        world.Add(preview);
        if (world.Find(BodyOf(creator)) is not null && world.CloneTree(BodyOf(creator)) is { } body)
            world.Modify(body.Id, obj => obj.ParentId = preview.Id);
    }

    static int BodyOf(WorldObject creator) =>
        creator.Data.Find(pair => pair.Key == Blueprint).Value is Pairs blueprint
        && blueprint.Find(pair => pair.Key == ChildrenMap).Value is Pairs map
        && map.Find(pair => pair.Key == BodyKey).Value is int body
            ? body
            : -1;

    static bool IsBody(WorldObject obj) =>
        obj.Type == WorldObjectType.Blueprint
        && obj.Data.Find(pair => pair.Key == Blueprint).Value is Pairs blueprint
        && blueprint.Find(pair => pair.Key == ClientSideType).Value is { } kind
        && Convert.ToInt32(kind) == (int)BlueprintType.Body;

    static Pairs WithBody(Pairs data, int body)
    {
        Pairs blueprint = data.Find(pair => pair.Key == Blueprint).Value as Pairs ?? [];
        Pairs map = blueprint.Find(pair => pair.Key == ChildrenMap).Value as Pairs ?? [];
        Pairs newMap = [.. map.Where(pair => pair.Key != BodyKey), (BodyKey, PackedType.Int32, body)];
        Pairs newBlueprint = [.. blueprint.Where(pair => pair.Key != ChildrenMap), (ChildrenMap, PackedType.Hashtable, newMap)];
        return [.. data.Where(pair => pair.Key != Blueprint), (Blueprint, PackedType.Hashtable, newBlueprint)];
    }
}
