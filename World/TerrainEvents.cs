using System.Buffers.Binary;

namespace OpenKogama.World;

public static class TerrainEvents
{
    public const byte CubeAdd = 1;
    public const byte CubeRemove = 2;
    public const byte AddedCubeRemove = 5;
    const int AddLength = 8;
    const int Length = 7;
    const int PositionLength = 6;

    public static int LengthOf(byte type) => type == CubeAdd ? AddLength : Length;

    public static bool Valid(byte[] runtimeEvent) => runtimeEvent.Length > 0 && runtimeEvent.Length == LengthOf(runtimeEvent[0]);

    public static (short X, short Y, short Z) Cube(byte[] runtimeEvent)
    {
        ReadOnlySpan<byte> position = runtimeEvent.AsSpan(runtimeEvent.Length - PositionLength);
        return (BinaryPrimitives.ReadInt16BigEndian(position), BinaryPrimitives.ReadInt16BigEndian(position[2..]), BinaryPrimitives.ReadInt16BigEndian(position[4..]));
    }

    public static List<byte[]> Compact(IEnumerable<byte[]> runtimeEvents)
    {
        var kept = new List<byte[]?>();
        var added = new Dictionary<(short, short, short), int>();
        foreach (byte[] runtimeEvent in runtimeEvents.Where(Valid))
        {
            byte type = runtimeEvent[0];
            if (type == CubeAdd) added[Cube(runtimeEvent)] = kept.Count;
            else if (type is CubeRemove or AddedCubeRemove && added.Remove(Cube(runtimeEvent), out int index))
            {
                kept[index] = null;
                continue;
            }
            kept.Add(runtimeEvent);
        }
        return [.. kept.OfType<byte[]>()];
    }
}
