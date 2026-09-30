using OpenKogama.World;

namespace OpenKogama.Game;

public static class AvatarMetaData
{
    public static void Write(BytePackerWriter writer, int bodyId, bool silver)
    {
        writer.WriteInt32(bodyId);
        writer.WriteString($"Avatar {bodyId}");
        if (silver) writer.WriteInt32(0);
        writer.WriteInt32(0);
        writer.WriteBool(false);
        writer.WriteBool(false);
    }

    public static byte[] Of(int bodyId, bool silver)
    {
        var writer = new BytePackerWriter();
        Write(writer, bodyId, silver);
        return writer.ToArray();
    }
}
