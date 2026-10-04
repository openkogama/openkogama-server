namespace OpenKogama.Game;

public static class CustomBadges
{
    public const int FirstLevel = 1000;

    static readonly List<(string Name, byte[] Png)> Icons = [];

    public static int Register(string name, byte[] png)
    {
        lock (Icons)
        {
            int index = Icons.FindIndex(icon => icon.Name == name);
            if (index >= 0) Icons[index] = (name, png);
            else
            {
                Icons.Add((name, png));
                index = Icons.Count - 1;
            }
            return FirstLevel + index;
        }
    }

    public static int? LevelOf(string name)
    {
        lock (Icons)
        {
            int index = Icons.FindIndex(icon => icon.Name == name);
            return index >= 0 ? FirstLevel + index : null;
        }
    }

    public static byte[]? Image(string name)
    {
        lock (Icons) return Icons.Find(icon => icon.Name == name).Png;
    }

    public static List<(int Level, string Name)> All()
    {
        lock (Icons) return [.. Icons.Select((icon, index) => (FirstLevel + index, icon.Name))];
    }
}
