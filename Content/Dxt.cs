using System.Buffers.Binary;

namespace OpenKogama.Content;

public static class Dxt
{
    public const int Dxt1Format = 10;
    public const int Dxt5Format = 12;

    public static int Size(int width, int height, bool alpha) => Math.Max(1, width / 4) * Math.Max(1, height / 4) * (alpha ? 16 : 8);

    public static byte[] Encode(Rgba image, bool alpha)
    {
        if (image.Width % 4 != 0 || image.Height % 4 != 0) throw new ArgumentException("a compressed texture needs sides that divide by 4");
        int blockSize = alpha ? 16 : 8;
        byte[] output = new byte[Size(image.Width, image.Height, alpha)];
        Span<byte> block = stackalloc byte[64];
        int at = 0;
        for (int by = 0; by < image.Height; by += 4)
            for (int bx = 0; bx < image.Width; bx += 4)
            {
                for (int row = 0; row < 4; row++)
                    image.Pixels.AsSpan(((by + row) * image.Width + bx) * 4, 16).CopyTo(block[(row * 16)..]);
                Span<byte> target = output.AsSpan(at, blockSize);
                if (alpha)
                {
                    AlphaBlock(block, target[..8]);
                    ColorBlock(block, target[8..]);
                }
                else
                {
                    ColorBlock(block, target);
                }
                at += blockSize;
            }
        return output;
    }

    static void ColorBlock(ReadOnlySpan<byte> pixels, Span<byte> target)
    {
        Span<float> mean = stackalloc float[3];
        for (int i = 0; i < 16; i++)
            for (int c = 0; c < 3; c++) mean[c] += pixels[i * 4 + c] / 16f;

        Span<float> axis = stackalloc float[3];
        PrincipalAxis(pixels, mean, axis);
        float lowest = float.MaxValue, highest = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float t = 0;
            for (int c = 0; c < 3; c++) t += (pixels[i * 4 + c] - mean[c]) * axis[c];
            lowest = Math.Min(lowest, t);
            highest = Math.Max(highest, t);
        }
        float inset = (highest - lowest) / 16;
        Span<float> first = stackalloc float[3];
        Span<float> second = stackalloc float[3];
        for (int c = 0; c < 3; c++)
        {
            first[c] = mean[c] + axis[c] * (highest - inset);
            second[c] = mean[c] + axis[c] * (lowest + inset);
        }

        int error = Fit(pixels, first, second, target);
        Span<byte> refined = stackalloc byte[8];
        if (Refine(pixels, target, first, second) && Fit(pixels, first, second, refined) < error) refined.CopyTo(target);
    }

    static void PrincipalAxis(ReadOnlySpan<byte> pixels, ReadOnlySpan<float> mean, Span<float> axis)
    {
        Span<float> covariance = stackalloc float[9];
        for (int i = 0; i < 16; i++)
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    covariance[a * 3 + b] += (pixels[i * 4 + a] - mean[a]) * (pixels[i * 4 + b] - mean[b]);

        int widest = covariance[0] >= covariance[4] && covariance[0] >= covariance[8] ? 0 : covariance[4] >= covariance[8] ? 1 : 2;
        covariance.Slice(widest * 3, 3).CopyTo(axis);
        Span<float> next = stackalloc float[3];
        for (int step = 0; step < 8; step++)
        {
            for (int a = 0; a < 3; a++) next[a] = covariance[a * 3] * axis[0] + covariance[a * 3 + 1] * axis[1] + covariance[a * 3 + 2] * axis[2];
            float length = MathF.Sqrt(next[0] * next[0] + next[1] * next[1] + next[2] * next[2]);
            if (length < 1e-6f) break;
            for (int a = 0; a < 3; a++) axis[a] = next[a] / length;
        }
        float size = MathF.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
        if (size < 1e-6f) axis.Fill(1 / MathF.Sqrt(3));
        else for (int a = 0; a < 3; a++) axis[a] /= size;
    }

    static int Fit(ReadOnlySpan<byte> pixels, ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<byte> target)
    {
        ushort high = To565(first), low = To565(second);
        if (high < low) (high, low) = (low, high);
        BinaryPrimitives.WriteUInt16LittleEndian(target, high);
        BinaryPrimitives.WriteUInt16LittleEndian(target[2..], low);

        Span<int> palette = stackalloc int[12];
        From565(high, palette[0..3]);
        From565(low, palette[3..6]);
        for (int c = 0; c < 3; c++)
        {
            palette[6 + c] = (2 * palette[c] + palette[3 + c]) / 3;
            palette[9 + c] = (palette[c] + 2 * palette[3 + c]) / 3;
        }

        int choices = high == low ? 1 : 4;
        int error = 0;
        uint indices = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int p = 0; p < choices; p++)
            {
                int distance = 0;
                for (int c = 0; c < 3; c++)
                {
                    int delta = pixels[i * 4 + c] - palette[p * 3 + c];
                    distance += delta * delta;
                }
                if (distance < bestDistance) (best, bestDistance) = (p, distance);
            }
            indices |= (uint)best << (i * 2);
            error += bestDistance;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(target[4..], indices);
        return error;
    }

    static bool Refine(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> block, Span<float> first, Span<float> second)
    {
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        float aa = 0, ab = 0, bb = 0;
        Span<float> ax = stackalloc float[3];
        Span<float> bx = stackalloc float[3];
        for (int i = 0; i < 16; i++)
        {
            float weight = (indices >> (i * 2) & 3) switch { 0 => 1f, 1 => 0f, 2 => 2f / 3, _ => 1f / 3 };
            float other = 1 - weight;
            aa += weight * weight;
            ab += weight * other;
            bb += other * other;
            for (int c = 0; c < 3; c++)
            {
                ax[c] += weight * pixels[i * 4 + c];
                bx[c] += other * pixels[i * 4 + c];
            }
        }
        float determinant = aa * bb - ab * ab;
        if (MathF.Abs(determinant) < 1e-4f) return false;
        for (int c = 0; c < 3; c++)
        {
            first[c] = (bb * ax[c] - ab * bx[c]) / determinant;
            second[c] = (aa * bx[c] - ab * ax[c]) / determinant;
        }
        return true;
    }

    static void AlphaBlock(ReadOnlySpan<byte> pixels, Span<byte> target)
    {
        int high = 0, low = 255;
        for (int i = 0; i < 16; i++)
        {
            int alpha = pixels[i * 4 + 3];
            if (alpha > high) high = alpha;
            if (alpha < low) low = alpha;
        }
        target[0] = (byte)high;
        target[1] = (byte)low;
        if (high == low)
        {
            target[2..8].Clear();
            return;
        }

        Span<int> palette = stackalloc int[8];
        palette[0] = high;
        palette[1] = low;
        for (int i = 1; i < 7; i++) palette[i + 1] = ((7 - i) * high + i * low) / 7;

        ulong indices = 0;
        for (int i = 0; i < 16; i++)
        {
            int alpha = pixels[i * 4 + 3];
            int best = 0, bestDistance = int.MaxValue;
            for (int p = 0; p < 8; p++)
            {
                int distance = Math.Abs(alpha - palette[p]);
                if (distance < bestDistance) (best, bestDistance) = (p, distance);
            }
            indices |= (ulong)best << (i * 3);
        }
        for (int i = 0; i < 6; i++) target[2 + i] = (byte)(indices >> (i * 8));
    }

    static ushort To565(ReadOnlySpan<float> rgb) =>
        (ushort)(Level(rgb[0], 31) << 11 | Level(rgb[1], 63) << 5 | Level(rgb[2], 31));

    static int Level(float value, int levels) => (int)MathF.Round(Math.Clamp(value, 0, 255) * levels / 255);

    static void From565(ushort color, Span<int> rgb)
    {
        int r = color >> 11 & 31, g = color >> 5 & 63, b = color & 31;
        rgb[0] = r << 3 | r >> 2;
        rgb[1] = g << 2 | g >> 4;
        rgb[2] = b << 3 | b >> 2;
    }
}
