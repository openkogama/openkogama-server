namespace OpenKogama.Api;

public interface ICubeModel : ITerrain
{
    int ObjectId { get; }
    float CubeSize { get; set; }
    bool Shared { get; }
    bool MakeUnique();
}
