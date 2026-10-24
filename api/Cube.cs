namespace OpenKogama.Api;

public sealed class Cube : IEquatable<Cube>
{
    static readonly byte[] Identity = [20, 120, 124, 24, 4, 104, 100, 0];

    readonly Material[] _faceMaterials;
    readonly byte[] _byteCorners;

    public Cube(Material material)
    {
        _faceMaterials = [material, material, material, material, material, material];
        _byteCorners = Identity;
    }

    public Cube(ReadOnlySpan<Material> faceMaterials, ReadOnlySpan<byte> byteCorners)
    {
        if (faceMaterials.Length != 6) throw new ArgumentException("a cube has 6 face materials", nameof(faceMaterials));
        if (byteCorners.Length != 8) throw new ArgumentException("a cube has 8 corners", nameof(byteCorners));
        foreach (byte corner in byteCorners)
            if (corner > 124) throw new ArgumentOutOfRangeException(nameof(byteCorners), corner, "a corner is a number from 0 to 124");

        _faceMaterials = faceMaterials.ToArray();
        _byteCorners = byteCorners.SequenceEqual(Identity) ? Identity : byteCorners.ToArray();
    }

    public static ReadOnlySpan<byte> IdentityByteCorners => Identity;

    public Material Material => _faceMaterials[(int)CubeFace.Top];

    public ReadOnlySpan<byte> ByteCorners => _byteCorners;

    public Material FaceMaterial(CubeFace face) => _faceMaterials[(int)face];

    public bool Equals(Cube? other) =>
        other is not null
        && ((ReadOnlySpan<Material>)_faceMaterials).SequenceEqual(other._faceMaterials)
        && ((ReadOnlySpan<byte>)_byteCorners).SequenceEqual(other._byteCorners);

    public override bool Equals(object? obj) => Equals(obj as Cube);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (Material material in _faceMaterials) hash.Add(material);
        hash.AddBytes(_byteCorners);
        return hash.ToHashCode();
    }

    public static bool operator ==(Cube? left, Cube? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Cube? left, Cube? right) => !(left == right);
}
