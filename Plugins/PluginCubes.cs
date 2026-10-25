using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Physics;
using OpenKogama.World;

namespace OpenKogama.Plugins;

static class PluginCubes
{
    public static List<CubeChange> Parse(byte[] changes)
    {
        var parsed = new List<CubeChange>();
        foreach ((CubeAction action, short x, short y, short z, byte[]? cube) in CubeModel.Changes(changes))
        {
            if (action == CubeAction.Deleted)
            {
                parsed.Add(new CubeChange(x, y, z, null));
                continue;
            }
            if (cube is null) continue;
            Cube shaped = CubeEdit.ToCube(cube);
            for (int step = 0; step < CubeModel.Row(cube); step++)
                parsed.Add(new CubeChange(x + step, y, z, shaped));
        }
        return parsed;
    }

    public static byte[] Revert(Prototype prototype, IEnumerable<CubeChange> changes)
    {
        var undo = new List<byte>();
        Span<byte> materials = stackalloc byte[6];
        foreach (CubeChange change in changes)
        {
            short x = (short)change.X, y = (short)change.Y, z = (short)change.Z;
            if (prototype.Cubes.Get(x, y, z) is not byte[] cube)
            {
                CubeModel.AppendDeleted(undo, x, y, z);
                continue;
            }
            for (int face = 0; face < materials.Length; face++) materials[face] = CubeShape.Material(cube, (Face)face);
            CubeModel.AppendAdded(undo, x, y, z, CubeShape.ByteCorners(cube), materials);
        }
        return [.. undo];
    }
}
