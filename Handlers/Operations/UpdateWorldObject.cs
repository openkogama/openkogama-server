using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateWorldObject(Session session) : IOperationHandler
{

    public byte Code => (byte)OperationCode.UpdateWorldObject;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? sender = session.For(peer);
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (Teleports.IsPrivate(objectId)) return;
        if (sender is not null
            && sender.Owns(objectId)
            && request.Parameters.TryGetValue((byte)ParameterKey.PosY, out object? y))
        {
            session.Round.TrackHeight(sender, Convert.ToSingle(y));
        }
        if (sender is not null && (objectId == sender.ActiveSpawnRole || session.World.Find(sender.ActiveSpawnRole)?.ParentId == objectId))
            Remember(sender, request);
        if (sender is not null && (sender.Owns(objectId) || sender.ExtraRole(objectId)))
            Place(objectId, request);

        var evt = new EventData((byte)EventCode.UpdateWorldObject)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        bool builder = sender is not null && sender.ExtraRole(objectId);
        foreach (Player player in session.Players)
            if (player.Peer != peer && player.InWorld && (!builder || player.SpawnRoles))
                player.Peer.Send(evt, reliable: false);
    }

    void Place(int objectId, OperationRequest request)
    {
        float[]? position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ);
        float[]? rotation = Rotation(request);
        int root = session.World.RootId;
        session.World.Modify(objectId, obj =>
        {
            if (obj.ParentId != root) return;
            if (position is not null) obj.Position = position;
            if (rotation is not null) obj.Rotation = rotation;
        });
    }

    static void Remember(Player sender, OperationRequest request)
    {
        if (Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ) is { } position) sender.LastPosition = position;
        if (Rotation(request) is { } rotation) sender.LastRotation = rotation;
    }

    static float[]? Rotation(OperationRequest request)
    {
        if (Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW) is { } rotation) return rotation;
        if (request[(byte)ParameterKey.ByteRotation] is not byte[] { Length: 3 } packed) return null;
        (float x, float y, float z, float w) = QuaternionCompression.ToQuaternion([packed[0], 0, 0]);
        return [x, y, z, w];
    }

    static float[]? Floats(OperationRequest request, params ParameterKey[] keys) =>
        keys.All(key => request.Parameters.ContainsKey((byte)key))
            ? [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))]
            : null;
}
