using System.Runtime.InteropServices;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Physics;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class CubeEdit(GameWorld world, Session? session, Prototype prototype, Player? viewer = null) : ICubeEdit
{
    const int BatchCubes = 2048;

    readonly List<byte> _changes = [];
    int _pending;

    public void Set(int x, int y, int z, Api.Material material)
    {
        int start = _changes.Count;
        CubeModel.AppendAdded(_changes, Coordinate(x), Coordinate(y), Coordinate(z), (byte)material);
        Added(start);
    }

    public void Set(int x, int y, int z, Cube cube)
    {
        int start = _changes.Count;
        Span<byte> materials = stackalloc byte[6];
        for (int i = 0; i < materials.Length; i++) materials[i] = (byte)cube.FaceMaterial((CubeFace)i);
        CubeModel.AppendAdded(_changes, Coordinate(x), Coordinate(y), Coordinate(z), cube.ByteCorners, materials);
        Added(start);
    }

    public void Remove(int x, int y, int z)
    {
        int start = _changes.Count;
        CubeModel.AppendDeleted(_changes, Coordinate(x), Coordinate(y), Coordinate(z));
        Added(start);
    }

    public void Dispose() => Send();

    void Added(int start)
    {
        if (viewer is null) prototype.Cubes.Apply(CollectionsMarshal.AsSpan(_changes)[start..]);
        if (++_pending >= BatchCubes) Send();
    }

    void Send()
    {
        if (_pending == 0) return;
        byte[] data = [.. _changes];
        _changes.Clear();
        _pending = 0;
        if (viewer is null) world.MarkChanged();
        if (session is null) return;

        var evt = new EventData((byte)EventCode.UpdatePrototype)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldInventoryID] = prototype.Id,
                [(byte)ParameterKey.WorldInventoryData] = data,
            },
        };
        foreach (Player player in viewer is null ? session.Players : [viewer])
            if (player.InWorld)
                player.Peer.Send(evt);
    }

    public static Cube ToCube(byte[] cube)
    {
        Span<Api.Material> faces = stackalloc Api.Material[6];
        for (int i = 0; i < faces.Length; i++) faces[i] = (Api.Material)CubeShape.Material(cube, (Face)i);
        return new Cube(faces, CubeShape.ByteCorners(cube));
    }

    static short Coordinate(int value) =>
        value is >= short.MinValue and <= short.MaxValue ? (short)value : throw new ArgumentOutOfRangeException(nameof(value), value, "cube coordinate out of range");
}
