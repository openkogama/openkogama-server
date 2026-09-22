namespace OpenKogama.Game;

public sealed class AvatarPart
{
    public string Bone { get; set; } = "";
    public float Scale { get; set; } = 0.125f;
    public string Cubes { get; set; } = "";

    public byte[] CubeData => Convert.FromBase64String(Cubes);
}
