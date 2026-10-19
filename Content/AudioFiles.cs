using System.Buffers.Binary;
using NLayer;
using NVorbis;

namespace OpenKogama.Content;

public static class AudioFiles
{
    public static readonly string[] Extensions = [".wav", ".ogg", ".mp3"];
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(15);
    const int FallbackRate = 44100;
    const int Chunk = 16384;

    public static Pcm Decode(byte[] data, string extension) => extension.ToLowerInvariant() switch
    {
        ".wav" => Wav(data),
        ".ogg" => Stream(data, (stream, sink) =>
        {
            using var vorbis = new VorbisReader(stream, false);
            sink.Start(vorbis.Channels, vorbis.SampleRate);
            float[] buffer = new float[Chunk * vorbis.Channels];
            for (int read; (read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0;) sink.Add(buffer.AsSpan(0, read));
        }),
        ".mp3" => Stream(data, (stream, sink) =>
        {
            using var mpeg = new MpegFile(stream);
            sink.Start(mpeg.Channels, mpeg.SampleRate);
            float[] buffer = new float[Chunk * mpeg.Channels];
            for (int read; (read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0;) sink.Add(buffer.AsSpan(0, read));
        }),
        _ => throw new NotSupportedException($"{extension} files are not supported"),
    };

    static Pcm Stream(byte[] data, Action<Stream, Sink> decode)
    {
        var sink = new Sink();
        using (var stream = new MemoryStream(data, writable: false)) decode(stream, sink);
        return sink.Finish();
    }

    static Pcm Wav(byte[] data)
    {
        ReadOnlySpan<byte> file = data;
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a wav file");

        int format = 0, channels = 0, rate = 0, bits = 0;
        var sink = new Sink();
        for (int at = 12; at + 8 <= file.Length;)
        {
            ReadOnlySpan<byte> id = file.Slice(at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]);
            ReadOnlySpan<byte> chunk = file.Slice(at + 8, Math.Min(size, file.Length - at - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                if (format == 0xFFFE && chunk.Length >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels == 0) throw new InvalidDataException("wav data comes before its format");
                sink.Start(channels, rate);
                int width = bits / 8;
                float[] buffer = new float[Chunk * channels];
                for (int offset = 0; offset + width <= chunk.Length;)
                {
                    int count = Math.Min(buffer.Length, (chunk.Length - offset) / width);
                    for (int i = 0; i < count; i++, offset += width)
                        buffer[i] = Sample(chunk.Slice(offset, width), format, bits);
                    sink.Add(buffer.AsSpan(0, count));
                }
                return sink.Finish();
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("wav file has no data");
    }

    static float Sample(ReadOnlySpan<byte> bytes, int format, int bits) => (format, bits) switch
    {
        (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        (1, 8) => (bytes[0] - 128) / 128f,
        (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
        (1, 24) => ((bytes[0] | (bytes[1] << 8) | (bytes[2] << 16)) << 8 >> 8) / 8388608f,
        (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
        _ => throw new NotSupportedException($"wav format {format} with {bits} bits is not supported"),
    };

    sealed class Sink
    {
        readonly MemoryStream _samples = new();
        int _channels, _rate;
        long _frames;

        public void Start(int channels, int rate)
        {
            if (channels < 1 || rate < 1) throw new InvalidDataException("the file has no audio");
            _channels = channels;
            _rate = rate;
        }

        public void Add(ReadOnlySpan<float> interleaved)
        {
            int frames = interleaved.Length / _channels;
            _frames += frames;
            if (_frames > MaxLength.TotalSeconds * _rate) throw new InvalidDataException($"the sound is longer than {MaxLength.TotalMinutes} minutes");

            Span<byte> pair = stackalloc byte[4];
            for (int frame = 0; frame < frames; frame++)
            {
                ReadOnlySpan<float> samples = interleaved.Slice(frame * _channels, _channels);
                float left = samples[0];
                float right = _channels > 1 ? samples[1] : left;
                for (int extra = 2; extra < _channels; extra++)
                {
                    left += samples[extra] * 0.5f;
                    right += samples[extra] * 0.5f;
                }
                BinaryPrimitives.WriteInt16LittleEndian(pair, Clip(left));
                BinaryPrimitives.WriteInt16LittleEndian(pair[2..], Clip(right));
                _samples.Write(_channels == 1 ? pair[..2] : pair);
            }
        }

        public Pcm Finish()
        {
            int channels = Math.Min(_channels, 2);
            byte[] samples = _samples.ToArray();
            return SoundBundles.Supports(_rate) ? new Pcm(samples, channels, _rate) : Resample(samples, channels, _rate);
        }

        static short Clip(float value) => (short)Math.Clamp(MathF.Round(value * 32767f), short.MinValue, short.MaxValue);

        static Pcm Resample(byte[] samples, int channels, int rate)
        {
            int frames = samples.Length / (2 * channels);
            int resized = (int)((long)frames * FallbackRate / rate);
            byte[] output = new byte[resized * 2 * channels];
            for (int frame = 0; frame < resized; frame++)
            {
                double position = (double)frame * rate / FallbackRate;
                int first = Math.Min((int)position, frames - 1);
                int second = Math.Min(first + 1, frames - 1);
                double weight = position - first;
                for (int channel = 0; channel < channels; channel++)
                {
                    short a = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan((first * channels + channel) * 2));
                    short b = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan((second * channels + channel) * 2));
                    BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan((frame * channels + channel) * 2), (short)Math.Round(a + (b - a) * weight));
                }
            }
            return new Pcm(output, channels, FallbackRate);
        }
    }
}
