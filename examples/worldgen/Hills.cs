namespace Worldgen;

sealed class Hills(int seed)
{
    public double Height(int x, int z) =>
        8 + 6 * Noise(x / 40.0, z / 40.0, 0) + 2 * Noise(x / 13.0, z / 13.0, 1);

    double Noise(double x, double z, int layer)
    {
        int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
        double dx = x - x0, dz = z - z0;
        double u = Fade(dx), v = Fade(dz);
        double near = Lerp(Gradient(x0, z0, layer, dx, dz), Gradient(x0 + 1, z0, layer, dx - 1, dz), u);
        double far = Lerp(Gradient(x0, z0 + 1, layer, dx, dz - 1), Gradient(x0 + 1, z0 + 1, layer, dx - 1, dz - 1), u);
        return Lerp(near, far, v);
    }

    double Gradient(int x, int z, int layer, double dx, double dz) => (Hash(x, z, layer) & 7) switch
    {
        0 => dx + dz,
        1 => dx - dz,
        2 => -dx + dz,
        3 => -dx - dz,
        4 => dx,
        5 => -dx,
        6 => dz,
        _ => -dz,
    };

    uint Hash(int x, int z, int layer)
    {
        uint h = (uint)seed * 0x9E3779B1u + (uint)x * 0x85EBCA77u + (uint)z * 0xC2B2AE3Du + (uint)layer * 0x27D4EB2Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
