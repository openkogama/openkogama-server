using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.World;

namespace OpenKogama.Plugins;

sealed class PluginTemplates : ITemplates
{
    public IReadOnlyList<string> Ids => [.. Templates.All.Select(template => template.Id)];

    public void Add(string id, string name, byte[] kgmap) => Add(id, name, kgmap, _ => { });

    public void Add(string id, string name, byte[] kgmap, Action<IWorldBuilder> build)
    {
        byte[] copy = [.. kgmap];
        Templates.Register(new WorldTemplate
        {
            Id = id,
            Name = name,
            Build = () =>
            {
                GameWorld world = WorldConverter.Import(copy, null, out _);
                build(new WorldEditor(world, null));
                return world;
            },
        });
    }

    public void Add(string id, string name, Action<IWorldBuilder> build, string? baseTemplate = null)
    {
        Templates.Register(new WorldTemplate
        {
            Id = id,
            Name = name,
            Build = () =>
            {
                GameWorld world = Templates.Create(baseTemplate);
                build(new WorldEditor(world, null));
                return world;
            },
        });
    }
}
