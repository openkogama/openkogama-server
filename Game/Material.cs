namespace OpenKogama.Game;

public sealed class Material
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public int Sound { get; set; }
    public int Modifier { get; set; }
    public string Animation { get; set; } = "";
    public int PriceGold { get; set; }
    public int PriceSilver { get; set; }
    public float[] Physical { get; set; } = [];

    public Material WithPath(string path)
    {
        var copy = (Material)MemberwiseClone();
        copy.Path = path;
        return copy;
    }
}
