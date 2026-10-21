using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenKogama.Content;

public sealed record Pcm(byte[] Samples, int Channels, int Rate)
{
    public int Frames => Samples.Length / (2 * Channels);
}

public sealed record SoundFile(byte[] Data, string Format);

// A sound bundle is one of the game's own sound bundles with the audio swapped. Each template keeps the
// archive, serialized file and target platform of its era, so every client gets the layout it expects.
public static class SoundBundles
{
    const int AudioClipClass = 83;
    const int UnityThreeVersion = 14;
    const int DecompressOnLoad = 0;
    const int PcmCompression = 0;
    const int VorbisType = 14;
    const int WavType = 20;
    const int FsbPcm16 = 2;
    const int FsbHeaderSize = 60;
    static readonly int[] FsbRates = [8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000];

    public static bool Supports(int rate) => Array.IndexOf(FsbRates, rate) >= 0;

    public static byte[] Build(byte[] template, Pcm audio, string seed, SoundFile? original = null)
    {
        (ArchiveFormat format, List<ArchiveNode> nodes) = UnityArchive.ReadWithFormat(template);
        int main = MainNode(nodes);

        string id = nodes[main].Name[(nodes[main].Name.IndexOf('-') + 1)..];
        string newId = Token(seed + "/cab", id.Length);
        var file = new SerializedFile(Replace(nodes[main].Data.ToArray(), id, newId));
        string name = new ByteReader(file.ToArray(), file.Offset(Clip(file))).Text();
        string token = Token(seed + "/name", name.Length);
        byte[] renamed = Replace(Replace(file.ToArray(), name, token), name.ToLowerInvariant(), token);
        nodes = [.. nodes.Select((node, i) => node with { Name = node.Name.Replace(id, newId), Data = i == main ? renamed : node.Data })];
        return Swap(format, nodes, main, audio, original);
    }

    public static byte[] Swap(byte[] bundle, Pcm audio, SoundFile? original = null)
    {
        (ArchiveFormat format, List<ArchiveNode> nodes) = UnityArchive.ReadWithFormat(bundle);
        return Swap(format, nodes, MainNode(nodes), audio, original);
    }

    static byte[] Swap(ArchiveFormat format, List<ArchiveNode> nodes, int main, Pcm audio, SoundFile? original)
    {
        var file = new SerializedFile(nodes[main].Data);
        SerializedFile.Entry clip = Clip(file);
        if (file.Version < UnityThreeVersion)
            return UnityArchive.Write(format, [nodes[main] with { Data = file.With(clip, EmbeddedClip(file.Data(clip), audio, original)) }]);

        bool web = file.Target == SerializedFile.WebGLTarget;
        byte[] resource = web ? BrowserAudio(audio, original) : Fsb(audio);
        byte[] serialized = file.ToArray();
        var reader = new ByteReader(serialized, file.Offset(clip));
        reader.Text();
        int loadType = reader.Position;
        reader.Skip(4);
        int channels = reader.Position;
        reader.Skip(28);
        reader.Text();
        int source = reader.Position;

        Span<byte> data = serialized;
        if (web) BinaryPrimitives.WriteInt32LittleEndian(data[loadType..], DecompressOnLoad);
        BinaryPrimitives.WriteInt32LittleEndian(data[channels..], audio.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(data[(channels + 4)..], audio.Rate);
        BinaryPrimitives.WriteInt32LittleEndian(data[(channels + 8)..], 16);
        BinaryPrimitives.WriteSingleLittleEndian(data[(channels + 12)..], (float)audio.Frames / audio.Rate);
        BinaryPrimitives.WriteUInt64LittleEndian(data[source..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(data[(source + 8)..], (ulong)resource.Length);
        if (!web) BinaryPrimitives.WriteInt32LittleEndian(data[(source + 16)..], PcmCompression);

        var built = new List<ArchiveNode>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i == main) built.Add(nodes[i] with { Data = serialized });
            else if (nodes[i].Name.EndsWith(".resource", StringComparison.Ordinal)) built.Add(nodes[i] with { Data = resource });
        }
        return UnityArchive.Write(format, built);
    }

    static int MainNode(List<ArchiveNode> nodes)
    {
        int main = nodes.FindIndex(node => !node.Name.Contains('.'));
        return main >= 0 ? main : throw new InvalidDataException("the sound bundle has no serialized file");
    }

    static SerializedFile.Entry Clip(SerializedFile file) =>
        file.Objects.FirstOrDefault(entry => entry.ClassId == AudioClipClass) ?? throw new InvalidDataException("the sound bundle has no AudioClip");

    // Unity 3 keeps the whole audio file inside the AudioClip: name, format, type, 3d, hardware, load type, then the bytes.
    static byte[] EmbeddedClip(ReadOnlySpan<byte> old, Pcm audio, SoundFile? original)
    {
        var reader = new ByteReader(old.ToArray());
        string name = reader.Text();
        int format = reader.Int();
        reader.Int();
        byte threeD = reader.Byte();
        byte hardware = reader.Byte();
        reader.Align(4);
        int stream = reader.Int();

        bool vorbis = original is { Format: "ogg" } && original.Data.AsSpan(0, Math.Min(original.Data.Length, 64)).IndexOf("vorbis"u8) >= 0;
        byte[] payload = vorbis ? original!.Data : Wav(audio);

        using var clip = new MemoryStream();
        byte[] text = Encoding.UTF8.GetBytes(name);
        WriteInt(clip, text.Length);
        clip.Write(text);
        Pad(clip);
        WriteInt(clip, format);
        WriteInt(clip, vorbis ? VorbisType : WavType);
        clip.WriteByte(threeD);
        clip.WriteByte(hardware);
        Pad(clip);
        WriteInt(clip, vorbis ? stream : DecompressOnLoad);
        WriteInt(clip, payload.Length);
        clip.Write(payload);
        Pad(clip);
        return clip.ToArray();
    }

    // WebGL hands the clip to the browser to decode, an mp3 stays as it is and anything else goes as wav.
    static byte[] BrowserAudio(Pcm audio, SoundFile? original) => original is { Format: "mp3" } ? original.Data : Wav(audio);

    public static byte[] Wav(Pcm audio)
    {
        byte[] wav = new byte[44 + audio.Samples.Length];
        Span<byte> span = wav;
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + audio.Samples.Length);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)audio.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], audio.Rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], audio.Rate * audio.Channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(audio.Channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], audio.Samples.Length);
        audio.Samples.CopyTo(span[44..]);
        return wav;
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

    static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    static void Pad(Stream stream)
    {
        while (stream.Position % 4 != 0) stream.WriteByte(0);
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
