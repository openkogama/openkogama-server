namespace OpenKogama.Storage;

public interface IImageStore
{
    byte[]? Image(int type, int id);
    void SaveImage(int type, int id, byte[] data);
    void DeleteImage(int type, int id);
}
