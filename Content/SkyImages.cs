using StbImageSharp;

namespace OpenKogama.Content;

public sealed class Rgba(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public bool Transparent
    {
        get
        {
            for (int i = 3; i < Pixels.Length; i += 4)
                if (Pixels[i] < 255) return true;
            return false;
        }
    }

    public static Rgba Decode(byte[] file)
    {
        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            throw new ArgumentException($"the image cannot be read: {error.Message}");
        }
        if (image.Width <= 0 || image.Height <= 0 || image.Width > SkyImages.MaxSide || image.Height > SkyImages.MaxSide)
            throw new ArgumentException($"an image can be at most {SkyImages.MaxSide} pixels on each side");
        return new Rgba(image.Width, image.Height, image.Data);
    }

    public Rgba OverBlack()
    {
        byte[] opaque = new byte[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            int alpha = Pixels[i + 3];
            opaque[i] = (byte)(Pixels[i] * alpha / 255);
            opaque[i + 1] = (byte)(Pixels[i + 1] * alpha / 255);
            opaque[i + 2] = (byte)(Pixels[i + 2] * alpha / 255);
            opaque[i + 3] = 255;
        }
        return new Rgba(Width, Height, opaque);
    }

    public Rgba Mask()
    {
        byte[] mask = new byte[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            int alpha = Pixels[i + 3];
            int brightness = (Pixels[i] * 299 + Pixels[i + 1] * 587 + Pixels[i + 2] * 114) / 1000;
            mask[i] = (byte)(brightness * alpha / 255);
            mask[i + 2] = (byte)(255 - alpha);
            mask[i + 3] = 255;
        }
        return new Rgba(Width, Height, mask);
    }

    public Rgba Glow()
    {
        byte[] mask = new byte[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            int light = (Pixels[i] * 299 + Pixels[i + 1] * 587 + Pixels[i + 2] * 114) / 1000 * Pixels[i + 3] / 255;
            mask[i] = (byte)light;
            mask[i + 2] = (byte)(255 - light);
            mask[i + 3] = 255;
        }
        return new Rgba(Width, Height, mask);
    }

    public Rgba MirrorColumns()
    {
        byte[] mirrored = new byte[Pixels.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Pixels.AsSpan((y * Width + x) * 4, 4).CopyTo(mirrored.AsSpan((y * Width + Width - 1 - x) * 4));
        return new Rgba(Width, Height, mirrored);
    }

    public byte[] Rgb()
    {
        byte[] rgb = new byte[Width * Height * 3];
        for (int i = 0, j = 0; i < Pixels.Length; i += 4, j += 3)
        {
            rgb[j] = Pixels[i];
            rgb[j + 1] = Pixels[i + 1];
            rgb[j + 2] = Pixels[i + 2];
        }
        return rgb;
    }

    public Rgba Crop(int x, int y, int width, int height)
    {
        byte[] cut = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Pixels.AsSpan(((y + row) * Width + x) * 4, width * 4).CopyTo(cut.AsSpan(row * width * 4));
        return new Rgba(width, height, cut);
    }

    public Rgba Resize(int width, int height)
    {
        if (width == Width && height == Height) return this;
        int stepsX = Math.Max(1, (int)Math.Ceiling((double)Width / width));
        int stepsY = Math.Max(1, (int)Math.Ceiling((double)Height / height));
        byte[] scaled = new byte[width * height * 4];
        Span<float> sum = stackalloc float[4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                sum.Clear();
                for (int sy = 0; sy < stepsY; sy++)
                    for (int sx = 0; sx < stepsX; sx++)
                        Sample(((x + (sx + 0.5f) / stepsX) / width) * Width - 0.5f, ((y + (sy + 0.5f) / stepsY) / height) * Height - 0.5f, sum);
                int at = (y * width + x) * 4;
                for (int c = 0; c < 4; c++) scaled[at + c] = (byte)Math.Clamp(MathF.Round(sum[c] / (stepsX * stepsY)), 0, 255);
            }
        return new Rgba(width, height, scaled);
    }

    public void Sample(float x, float y, Span<float> sum, bool wrapX = false)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        for (int corner = 0; corner < 4; corner++)
        {
            int px = x0 + (corner & 1), py = y0 + (corner >> 1);
            px = wrapX ? (px % Width + Width) % Width : Math.Clamp(px, 0, Width - 1);
            py = Math.Clamp(py, 0, Height - 1);
            float weight = ((corner & 1) == 0 ? 1 - fx : fx) * ((corner >> 1) == 0 ? 1 - fy : fy);
            int at = (py * Width + px) * 4;
            for (int c = 0; c < 4; c++) sum[c] += Pixels[at + c] * weight;
        }
    }

    public Rgba FlipRows()
    {
        byte[] flipped = new byte[Pixels.Length];
        int row = Width * 4;
        for (int y = 0; y < Height; y++)
            Pixels.AsSpan(y * row, row).CopyTo(flipped.AsSpan((Height - 1 - y) * row));
        return new Rgba(Width, Height, flipped);
    }
}

public static class SkyImages
{
    public const int MaxSide = 8192;

    public static int FaceSize(Rgba image, int largest)
    {
        int side = Layout(image) switch
        {
            CubeLayout.Panorama => image.Width / 4,
            CubeLayout.Strip => image.Height,
            _ => image.Width / 4,
        };
        int size = 64;
        while (size < side && size < largest) size *= 2;
        return Math.Min(size, largest);
    }

    public static Rgba[] Faces(Rgba image, int size)
    {
        CubeLayout layout = Layout(image);
        var faces = new Rgba[6];
        for (int face = 0; face < 6; face++)
        {
            if (layout == CubeLayout.Strip)
            {
                faces[face] = image.Crop(face * image.Height, 0, image.Height, image.Height).Resize(size, size);
                continue;
            }
            if (layout == CubeLayout.Cross)
            {
                int side = image.Width / 4;
                (int column, int row) = CrossCells[face];
                faces[face] = image.Crop(column * side, row * side, side, side).Resize(size, size);
                continue;
            }
            faces[face] = FromPanorama(image, face, size);
        }
        return faces;
    }

    enum CubeLayout { Panorama, Strip, Cross }

    static readonly (int Column, int Row)[] CrossCells = [(2, 1), (0, 1), (1, 0), (1, 2), (1, 1), (3, 1)];

    static CubeLayout Layout(Rgba image)
    {
        if (image.Width == image.Height * 2) return CubeLayout.Panorama;
        if (image.Width == image.Height * 6) return CubeLayout.Strip;
        if (image.Width * 3 == image.Height * 4) return CubeLayout.Cross;
        throw new ArgumentException("a sky texture needs a 2:1 panorama, a 6:1 strip of +X -X +Y -Y +Z -Z faces or a 4:3 cross");
    }

    static Rgba FromPanorama(Rgba panorama, int face, int size)
    {
        byte[] pixels = new byte[size * size * 4];
        Span<float> sum = stackalloc float[4];
        int Steps = Math.Clamp((int)Math.Ceiling(panorama.Width / 4.0 / size), 1, 4);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                sum.Clear();
                for (int sy = 0; sy < Steps; sy++)
                    for (int sx = 0; sx < Steps; sx++)
                    {
                        float u = (x + (sx + 0.5f) / Steps) / size * 2f - 1f;
                        float v = (y + (sy + 0.5f) / Steps) / size * 2f - 1f;
                        (float dx, float dy, float dz) = face switch
                        {
                            0 => (1f, -v, -u),
                            1 => (-1f, -v, u),
                            2 => (u, 1f, v),
                            3 => (u, -1f, -v),
                            4 => (u, -v, 1f),
                            _ => (-u, -v, -1f),
                        };
                        float longitude = MathF.Atan2(dx, dz);
                        float latitude = MathF.Atan2(dy, MathF.Sqrt(dx * dx + dz * dz));
                        float px = (longitude / (2f * MathF.PI) + 0.5f) * panorama.Width - 0.5f;
                        float py = (0.5f - latitude / MathF.PI) * panorama.Height - 0.5f;
                        panorama.Sample(px, py, sum, wrapX: true);
                    }
                int at = (y * size + x) * 4;
                for (int c = 0; c < 4; c++) pixels[at + c] = (byte)Math.Clamp(MathF.Round(sum[c] / (Steps * Steps)), 0, 255);
            }
        return new Rgba(size, size, pixels);
    }
}
