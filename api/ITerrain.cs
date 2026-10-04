namespace OpenKogama.Api;

public interface ITerrain
{
    int Count { get; }
    void Set(int x, int y, int z, byte material);
    void Remove(int x, int y, int z);
    int Fill(int x1, int y1, int z1, int x2, int y2, int z2, byte material);
    void Clear();
}
