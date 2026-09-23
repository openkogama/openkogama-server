using OpenKogama.Game;

namespace OpenKogama.World;

public sealed class Prototype(int id, float scale, int authorId, CubeModel cubes)
{
    public int Id => id;
    public float Scale => scale;
    public int AuthorId => authorId;
    public CubeModel Cubes => cubes;
}
