namespace OpenKogama.Physics;

readonly record struct MaterialPhysics(float Friction, float Bounciness, float StaticFriction)
{
    public static readonly MaterialPhysics Air = new(0f, 0f, 0f);

    static readonly Lazy<Dictionary<int, MaterialPhysics>> All = new(Load);

    public static MaterialPhysics For(byte id) =>
        All.Value.TryGetValue(id, out MaterialPhysics found) ? found : All.Value.GetValueOrDefault(0, Air);

    static Dictionary<int, MaterialPhysics> Load()
    {
        var materials = new Dictionary<int, MaterialPhysics>();
        foreach (string version in (ReadOnlySpan<string>)["2025", "2015"])
            foreach (Game.Material material in Game.Materials.For(version))
                if (material.Physical is [float friction, float bounciness, _, float staticFriction, ..])
                    materials[material.Id] = new MaterialPhysics(friction, bounciness, staticFriction);
        return materials;
    }
}
