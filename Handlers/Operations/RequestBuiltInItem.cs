using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestBuiltInItem(Session session) : IOperationHandler
{
    const byte CubeModelItem = 1;
    const byte GroupItem = 2;

    public byte Code => (byte)OperationCode.RequestBuiltInItem;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        byte type = Convert.ToByte(request[(byte)ParameterKey.BuiltInType]);
        GameWorld world = session.World;

        var obj = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectGroupID]),
            ItemId = type,
            Position = Floats(request, ParameterKey.PosX, ParameterKey.PosY, ParameterKey.PosZ),
            Rotation = Floats(request, ParameterKey.RotX, ParameterKey.RotY, ParameterKey.RotZ, ParameterKey.RotW),
            Scale = Floats(request, ParameterKey.ScaleX, ParameterKey.ScaleY, ParameterKey.ScaleZ),
            Owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]),
        };
        List<Prototype> prototypes = [];

        switch (type)
        {
            case CubeModelItem:
                var data = (PhotonDictionary)request[(byte)ParameterKey.Data]!;
                float cubeScale = Convert.ToSingle(data[(byte)1]);
                byte material = Convert.ToByte(data[(byte)2]);

                var prototype = new Prototype(world.NewPrototypeId(), cubeScale, 1, CubeModel.SingleCube(material));
                prototypes.Add(prototype);
                obj.Type = WorldObjectType.CubeModel;
                obj.Data = [("protoTypeID", PackedType.Int32, prototype.Id)];
                break;

            case GroupItem:
                obj.Type = WorldObjectType.Group;
                obj.Owner = 0;
                break;

            default:
                Console.WriteLine($"peer {peer.Id}: unknown built-in item {type}");
                return;
        }

        foreach (Prototype prototype in prototypes) world.Add(prototype);
        world.Add(obj);
        world.MarkChanged();

        var evt = new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.Data] = WorldSerializer.Write(prototypes, [obj]),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);

        Console.WriteLine($"peer {peer.Id}: placed {obj.Type} {obj.Id}");
    }

    static float[] Floats(OperationRequest request, params ParameterKey[] keys) =>
        [.. keys.Select(key => Convert.ToSingle(request[(byte)key]))];
}
