namespace OpenKogama.Api;

public sealed class CubeChanges(IPlayer player, IWorld world, ICubeModel? model, IReadOnlyList<CubeChange> changes)
{
    public IPlayer Player { get; } = player;
    public IWorld World { get; } = world;
    public ICubeModel? Model { get; } = model;
    public IReadOnlyList<CubeChange> Changes { get; } = changes;
    public bool Cancel { get; set; }
}
