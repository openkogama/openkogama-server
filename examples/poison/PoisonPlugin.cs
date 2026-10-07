using System.Numerics;
using OpenKogama.Api;

namespace Poison;

public sealed class PoisonPlugin : IPlugin
{
    const byte Poison = 28;
    const float TerrainScale = 4f;
    const int Floor = 2;
    const int Ring = 4;
    const int WallHeight = 2;

    public void Load(IServer server) => server.Templates.Add("poison", "Poison Showcase", Build);

    static void Build(IWorldBuilder world)
    {
        Vector3 spawn = world.Objects.FirstOrDefault(obj => obj.Type.StartsWith("SpawnPoint"))?.Position ?? Vector3.Zero;
        int cx = (int)MathF.Floor(spawn.X / TerrainScale);
        int cz = (int)MathF.Floor(spawn.Z / TerrainScale);
        int y = (int)MathF.Floor(spawn.Y / TerrainScale);

        world.Terrain.Fill(cx - Floor, y, cz - Floor, cx + Floor, y, cz + Floor, Poison);
        for (int x = cx - Ring; x <= cx + Ring; x++)
            for (int z = cz - Ring; z <= cz + Ring; z++)
                if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) == Ring)
                    world.Terrain.Fill(x, y + 1, z, x, y + WallHeight, z, Poison);
    }
}
