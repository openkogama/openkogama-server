using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class RegisterWorldObject(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RegisterWorldObject;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        int owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);
        var data = request[(byte)ParameterKey.WorldObjectData] is { } raw
            ? (Dictionary<object, object?>)PhotonValues.Normalize(raw)!
            : [];

        var obj = new WorldObject
        {
            Id = session.World.NewObjectId(),
            ParentId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectGroupID]),
            Type = (WorldObjectType)Convert.ToInt32(request[(byte)ParameterKey.WorldObjectType]),
            Position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ),
            Rotation = Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW),
            Scale = Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ),
            Data = PackedData.FromPhoton(data),
            Owner = owner == 0 ? null : owner,
        };
        session.World.Add(obj);
        session.World.MarkChanged();

        peer.Send(new OperationResponse(request));

        GetNextGameBatch.SendAdded(session, player.Actor, session.World.SubtreeSnapshot(obj.Id));
        Plugins.PluginHost.Added(session, obj, player);

        Console.WriteLine($"peer {peer.Id}: registered {obj.Type} as {obj.Id}");
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
