using OpenKogama.Api;
using OpenKogama.Content;

namespace OpenKogama.Plugins;

sealed class PluginScripts : IScripts
{
    public IReadOnlyList<string> All => ClientScripts.Names;

    public bool Add(string name, IReadOnlyDictionary<string, byte[]> files) => ClientScripts.Add(name, files);

    public bool Remove(string name) => ClientScripts.Remove(name);
}
