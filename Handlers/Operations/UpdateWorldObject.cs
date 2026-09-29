using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateWorldObject(Session session) : IOperationHandler
{

    public byte Code => (byte)OperationCode.UpdateWorldObject;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? sender = session.For(peer);
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (sender is not null
            && objectId == sender.AvatarId
            && request.Parameters.TryGetValue((byte)ParameterKey.PosY, out object? y))
        {
            session.Round.TrackHeight(sender, Convert.ToSingle(y));
        }
        if (sender is not null && objectId == sender.ActiveSpawnRole)
            Remember(sender, request);

        var evt = new EventData((byte)EventCode.UpdateWorldObject)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        bool builder = sender is not null && objectId == sender.BuildAvatarId;
        foreach (Player player in session.Players)
            if (player.Peer != peer && (!builder || player.SpawnRoles))
                player.Peer.Send(evt, reliable: false);
    }

    static void Remember(Player sender, OperationRequest request)
    {
        if (Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ) is { } position) sender.LastPosition = position;
        if (Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW) is { } rotation) sender.LastRotation = rotation;
    }

    static float[]? Floats(OperationRequest request, params ParameterKey[] keys) =>
        keys.All(key => request.Parameters.ContainsKey((byte)key))
            ? [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))]
            : null;
}
