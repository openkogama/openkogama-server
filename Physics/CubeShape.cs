using System.Numerics;

namespace OpenKogama.Physics;

enum Face { Top, Bottom, Front, Back, Left, Right }

[Flags]
enum FaceFlags : byte { Top = 1, Bottom = 2, Front = 4, Back = 8, Left = 16, Right = 32 }

static class CubeShape
{
    const byte DefaultCorners = 1;
    const byte OneMaterial = 2;

    public static readonly FaceFlags[] AllFaces = [FaceFlags.Top, FaceFlags.Bottom, FaceFlags.Front, FaceFlags.Back, FaceFlags.Left, FaceFlags.Right];

    static readonly byte[] IdentityCorners = [20, 120, 124, 24, 4, 104, 100, 0];

    static readonly (int[] Corners, Face Face)[] FaceCorners =
    [
        ([0, 1, 2, 3], Face.Top),
        ([4, 5, 6, 7], Face.Bottom),
        ([7, 6, 1, 0], Face.Front),
        ([5, 4, 3, 2], Face.Back),
        ([4, 7, 0, 3], Face.Left),
        ([6, 5, 2, 1], Face.Right),
    ];

    public static Face ToFace(FaceFlags flag) => flag switch
    {
        FaceFlags.Bottom => Face.Bottom,
        FaceFlags.Front => Face.Front,
        FaceFlags.Back => Face.Back,
        FaceFlags.Left => Face.Left,
        FaceFlags.Right => Face.Right,
        _ => Face.Top,
    };

    public static ReadOnlySpan<byte> ByteCorners(byte[] cube) => (cube[0] & DefaultCorners) != 0 ? IdentityCorners : cube.AsSpan(1, 8);

    public static byte Material(byte[] cube, Face face)
    {
        if ((cube[0] & OneMaterial) != 0) return cube[^1];
        int start = (cube[0] & DefaultCorners) != 0 ? 1 : 9;
        return cube[start + (int)face];
    }

    public static Vector3 Corner(byte value) => new(-0.5f + value / 25 * 0.25f, -0.5f + value / 5 % 5 * 0.25f, -0.5f + value % 5 * 0.25f);

    public static void Corners(byte[] cube, Span<Vector3> corners)
    {
        ReadOnlySpan<byte> bytes = ByteCorners(cube);
        for (int i = 0; i < 8; i++) corners[i] = Corner(bytes[i]);
    }

    public static void FaceVertices(ReadOnlySpan<Vector3> corners, Face face, Span<Vector3> vertices)
    {
        int[] indices = FaceCorners[(int)face].Corners;
        for (int i = 0; i < 4; i++) vertices[i] = corners[indices[i]];
    }

    public static byte UnIndentedSides(byte[] cube)
    {
        ReadOnlySpan<byte> corners = ByteCorners(cube);
        Span<bool> moved = stackalloc bool[8];
        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            moved[i] = corners[i] != IdentityCorners[i];
            if (moved[i]) count++;
        }
        if (count == 0) return 63;
        if (count > 4) return 0;

        byte sides = 0;
        if (!moved[0] && !moved[1] && !moved[2] && !moved[3]) sides |= 1;
        if (!moved[4] && !moved[5] && !moved[6] && !moved[7]) sides |= 2;
        if (!moved[2] && !moved[3] && !moved[4] && !moved[5]) sides |= 8;
        if (!moved[0] && !moved[1] && !moved[6] && !moved[7]) sides |= 4;
        if (!moved[0] && !moved[3] && !moved[4] && !moved[7]) sides |= 16;
        if (!moved[1] && !moved[2] && !moved[5] && !moved[6]) sides |= 32;
        return sides;
    }

    public static bool Hides(byte[] cube, byte[] neighbor, FaceFlags side, FaceFlags opposite)
    {
        if ((UnIndentedSides(cube) & (byte)side) != 0 && (UnIndentedSides(neighbor) & (byte)opposite) != 0) return true;

        Span<Vector3> cubeCorners = stackalloc Vector3[8];
        Span<Vector3> neighborCorners = stackalloc Vector3[8];
        Span<Vector3> face = stackalloc Vector3[4];
        Span<Vector3> other = stackalloc Vector3[4];
        Corners(cube, cubeCorners);
        Corners(neighbor, neighborCorners);
        Face cubeFace = ToFace(side);
        Face neighborFace = ToFace(opposite);
        FaceVertices(cubeCorners, cubeFace, face);
        if (!TouchesBorder(cubeFace, face)) return false;
        FaceVertices(neighborCorners, neighborFace, other);
        if (!TouchesBorder(neighborFace, other)) return false;

        switch (cubeFace)
        {
            case Face.Top:
            case Face.Bottom:
                for (int i = 0; i < 4; i++)
                    if (face[i].X != other[3 - i].X || face[i].Z != other[3 - i].Z) return false;
                return true;
            case Face.Left:
            case Face.Right:
                return face[0].Z == other[1].Z && face[0].Y == other[1].Y && face[1].Z == other[0].Z && face[1].Y == other[0].Y
                    && face[2].Z == other[3].Z && face[2].Y == other[3].Y && face[3].Z == other[2].Z && face[3].Y == other[2].Y;
            default:
                return face[0].X == other[1].X && face[0].Y == other[1].Y && face[1].X == other[0].X && face[1].Y == other[0].Y
                    && face[2].X == other[3].X && face[2].Y == other[3].Y && face[3].X == other[2].X && face[3].Y == other[2].Y;
        }
    }

    static bool TouchesBorder(Face face, ReadOnlySpan<Vector3> vertices)
    {
        (int axis, float value) = face switch
        {
            Face.Top => (1, 0.5f),
            Face.Bottom => (1, -0.5f),
            Face.Front => (2, -0.5f),
            Face.Back => (2, 0.5f),
            Face.Left => (0, -0.5f),
            _ => (0, 0.5f),
        };
        foreach (Vector3 vertex in vertices)
            if ((axis == 0 ? vertex.X : axis == 1 ? vertex.Y : vertex.Z) != value) return false;
        return true;
    }
}
