using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class TransferOwnership(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.TransferOwnership;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        int owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);
        bool finalize = Convert.ToBoolean(request[(byte)ParameterKey.FinalizeTransform]);

        bool found = session.World.Modify(objectId, obj =>
        {
            obj.Owner = owner;
            if (!finalize) return;

            obj.Position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ);
            obj.Rotation = Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW);
            if (request.Parameters.ContainsKey((byte)ParameterKey.ScaleX))
                obj.Scale = Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ);
        });

        if (!found)
        {
            Console.WriteLine($"peer {peer.Id}: transfer ownership of unknown object {objectId}");
            return;
        }

        if (finalize) session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.TransferOwnership)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer)
                player.Peer.Send(evt);

        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.OwnerActorNr] = owner,
            },
        });
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
