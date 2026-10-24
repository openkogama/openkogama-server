using System.Numerics;

namespace OpenKogama.Api;

public interface ITerrain
{
    int Count { get; }
    Cube? Get(int x, int y, int z);
    IReadOnlyList<(int X, int Y, int Z)> Positions();
    void Set(int x, int y, int z, byte material);
    void Set(int x, int y, int z, Cube cube);
    void Remove(int x, int y, int z);
    int Fill(int x1, int y1, int z1, int x2, int y2, int z2, byte material);
    void Clear();
    ICubeEdit Edit();
    (int X, int Y, int Z) CubeAt(Vector3 position);
}
