namespace OpenKogama.Api;

public interface ICubeEdit : IDisposable
{
    void Set(int x, int y, int z, Material material);
    void Set(int x, int y, int z, Cube cube);
    void Remove(int x, int y, int z);
}
