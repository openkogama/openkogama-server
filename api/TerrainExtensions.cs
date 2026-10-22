namespace OpenKogama.Api;

public static class TerrainExtensions
{
    public static void Set(this ITerrain terrain, int x, int y, int z, Material material) =>
        terrain.Set(x, y, z, (byte)material);

    public static int Fill(this ITerrain terrain, int x1, int y1, int z1, int x2, int y2, int z2, Material material) =>
        terrain.Fill(x1, y1, z1, x2, y2, z2, (byte)material);
}
