using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenKogama.Content;

public sealed record Pcm(byte[] Samples, int Channels, int Rate)
{
    public int Frames => Samples.Length / (2 * Channels);
}

public static class SoundBundles
{
    const int AudioClipClass = 83;
    const int PcmCompression = 0;
    const int FsbPcm16 = 2;
    const int FsbHeaderSize = 60;
    static readonly int[] FsbRates = [8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000];

    public static bool Supports(int rate) => Array.IndexOf(FsbRates, rate) >= 0;

    public static byte[] Build(byte[] template, Pcm audio, string seed)
    {
        var file = new SerializedFile(template);
        SerializedFile.Entry clip = file.Objects.FirstOrDefault(entry => entry.ClassId == AudioClipClass)
            ?? throw new InvalidDataException("the sound template has no AudioClip");

        var reader = new ByteReader(template, file.Offset(clip));
        string name = reader.Text();
        reader.Skip(4);
        int format = reader.Position;
        reader.Skip(28);
        string cab = reader.Text().Split('/')[1]["CAB-".Length..];
        int resource = reader.Position;

        byte[] fsb = Fsb(audio);
        string newCab = Token(seed + "/cab", cab.Length);
        byte[] serialized = Replace(Replace(file.ToArray(), cab, newCab), name, Token(seed + "/name", name.Length));
        Span<byte> data = serialized;
        BinaryPrimitives.WriteInt32LittleEndian(data[format..], audio.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(data[(format + 4)..], audio.Rate);
        BinaryPrimitives.WriteInt32LittleEndian(data[(format + 8)..], 16);
        BinaryPrimitives.WriteSingleLittleEndian(data[(format + 12)..], (float)audio.Frames / audio.Rate);
        BinaryPrimitives.WriteUInt64LittleEndian(data[resource..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(data[(resource + 8)..], (ulong)fsb.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data[(resource + 16)..], PcmCompression);

        return UnityArchive.Write(file.Engine,
        [
            new ArchiveNode("CAB-" + newCab, serialized, UnityArchive.SerializedFileNode),
            new ArchiveNode("CAB-" + newCab + ".resource", fsb, 0),
        ]);
    }

    static byte[] Fsb(Pcm audio)
    {
        int headers = (FsbHeaderSize + 8 + 31) / 32 * 32 - FsbHeaderSize;
        ulong channels = audio.Channels == 2 ? 1UL : 0UL;
        ulong sample = ((ulong)audio.Frames << 34) | (channels << 5) | ((ulong)(Array.IndexOf(FsbRates, audio.Rate) + 1) << 1);

        byte[] fsb = new byte[FsbHeaderSize + headers + audio.Samples.Length];
        Span<byte> span = fsb;
        "FSB5"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], headers);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], audio.Samples.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], FsbPcm16);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], 1);
        MD5.HashData(audio.Samples).CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt64LittleEndian(span[FsbHeaderSize..], sample);
        audio.Samples.CopyTo(span[(FsbHeaderSize + headers)..]);
        return fsb;
    }

    static string Token(string seed, int length)
    {
        string hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        return string.Concat(Enumerable.Repeat(hex, length / hex.Length + 1))[..length];
    }

    static byte[] Replace(byte[] data, string old, string replacement)
    {
        byte[] from = Encoding.ASCII.GetBytes(old);
        byte[] to = Encoding.ASCII.GetBytes(replacement);
        for (int at = data.AsSpan().IndexOf(from); at >= 0;)
        {
            to.CopyTo(data, at);
            int next = data.AsSpan(at + to.Length).IndexOf(from);
            at = next < 0 ? -1 : at + to.Length + next;
        }
        return data;
    }
}
