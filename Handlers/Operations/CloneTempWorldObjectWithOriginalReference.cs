using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class CloneTempWorldObjectWithOriginalReference(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.CloneTempWorldObjectWithOriginalReference;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int originalId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        int owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);
        float[] position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ);
        float[] rotation = Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW);

        if (CollectTheItem.DropTemporary(session, originalId, position, rotation) is not { } clone)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        var evt = new EventData((byte)EventCode.CloneTempWorldObjectWithOriginalReferenceEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = new[] { originalId, clone.Id },
                [(byte)ParameterKey.OwnerActorNr] = owner,
                [(byte)ParameterKey.LinkID] = -1,
                [(byte)ParameterKey.ObjectLinkID] = -1,
                [(byte)ParameterKey.CloneToRootGroup] = true,
                [(byte)ParameterKey.PreviewProfileOwnerID] = 0,
                [(byte)ParameterKey.PosX] = position[0],
                [(byte)ParameterKey.PosY] = position[1],
                [(byte)ParameterKey.PosZ] = position[2],
                [(byte)ParameterKey.RotX] = rotation[0],
                [(byte)ParameterKey.RotY] = rotation[1],
                [(byte)ParameterKey.RotZ] = rotation[2],
                [(byte)ParameterKey.RotW] = rotation[3],
            },
        };
        foreach (Player player in session.Players)
            if (player.Peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(EventCode.CloneTempWorldObjectWithOriginalReferenceEvent))
                player.Peer.Send(evt);

        peer.Send(new OperationResponse(request) { Parameters = { [(byte)ParameterKey.WorldObjectID] = clone.Id } });
        Console.WriteLine($"peer {peer.Id}: dropped collectable {originalId} as {clone.Id}");
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => request[(byte)key] is { } value ? Convert.ToSingle(value) : 0f)];
}
