using OpenKogama.World;

namespace OpenKogama.Game;

public static class AvatarMetaData
{
    public static void Write(BytePackerWriter writer, int bodyId)
    {
        writer.WriteInt32(bodyId);
        writer.WriteString($"Avatar {bodyId}");
        writer.WriteInt32(0);
        writer.WriteInt32(0);
        writer.WriteBool(false);
        writer.WriteBool(false);
    }

    public static byte[] Of(int bodyId)
    {
        var writer = new BytePackerWriter();
        Write(writer, bodyId);
        return writer.ToArray();
    }
}
