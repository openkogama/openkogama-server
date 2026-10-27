namespace OpenKogama.Content;

public enum TextureLook { Picture, Light, Mask, Glow }

public static class TextureBundles
{
    public const byte Revision = 6;
    const int Rgb24Format = 3;
    const int Texture2DClass = 28;
    const int CubemapClass = 89;
    const int LeftoverSide = 4;

    public static byte[] Build(byte[] template, Rgba image, TextureLook look, string seed)
    {
        (ArchiveFormat format, List<ArchiveNode> nodes) = UnityArchive.ReadWithFormat(template);
        int main = nodes.FindIndex(node => (node.Flags & UnityArchive.SerializedFileNode) != 0 && !node.Name.Contains('.'));
        if (main < 0) throw new InvalidDataException("the texture bundle has no serialized file");

        var file = new SerializedFile(nodes[main].Data);
        bool cube = file.Objects.Any(entry => entry.ClassId == CubemapClass);
        Rgba source = look switch
        {
            TextureLook.Mask => image.Mask(),
            TextureLook.Glow => image.Glow(),
            TextureLook.Light => image.OverBlack(),
            _ => image,
        };
        bool alpha = look == TextureLook.Picture && source.Transparent;

        foreach (long pathId in file.Objects.Where(entry => entry.ClassId is Texture2DClass or CubemapClass).Select(entry => entry.PathId).ToList())
        {
            SerializedFile.Entry entry = file.Objects.First(candidate => candidate.PathId == pathId);
            TypeNode tree = file.Tree(entry) ?? throw new InvalidDataException("the texture bundle has no type tree");
            var texture = (StructValue)tree.ReadValue(file.Data(entry).ToArray());

            int width, height;
            byte[] data;
            if (entry.ClassId == CubemapClass)
            {
                width = height = SkyImages.FaceSize(source, texture.Int("m_Width"));
                data = [.. SkyImages.Faces(source, width).SelectMany(face => Dxt.Encode(face, alpha))];
            }
            else if (cube)
            {
                width = height = LeftoverSide;
                data = Dxt.Encode(new Rgba(LeftoverSide, LeftoverSide, new byte[LeftoverSide * LeftoverSide * 4]), alpha);
            }
            else
            {
                width = texture.Int("m_Width");
                height = texture.Int("m_Height");
                Rgba sized = source.Resize(width, height).FlipRows();
                data = look == TextureLook.Mask ? sized.MirrorColumns().Rgb() : Dxt.Encode(sized, alpha);
            }

            Fill(texture, width, height, entry.ClassId != CubemapClass && !cube && look == TextureLook.Mask ? Rgb24Format : alpha ? Dxt.Dxt5Format : Dxt.Dxt1Format, data);
            file = new SerializedFile(file.With(entry, tree.WriteValue(texture)));
        }

        string cab = nodes[main].Name;
        string id = cab[(cab.IndexOf('-') + 1)..];
        string newId = BundleIds.Token(seed + "/cab", id.Length);
        bool streamed = file.Objects.Any(entry => entry.ClassId is not (Texture2DClass or CubemapClass) && file.Data(entry).IndexOf(".resS"u8) >= 0);
        byte[] serialized = BundleIds.Replace(file.ToArray(), id, newId);
        List<ArchiveNode> built = [.. nodes
            .Select((node, index) => (node, index))
            .Where(pair => pair.index == main || streamed || !pair.node.Name.EndsWith(".resS", StringComparison.Ordinal))
            .Select(pair => pair.node with { Name = pair.node.Name.Replace(id, newId), Data = pair.index == main ? serialized : pair.node.Data })];
        return UnityArchive.Write(format, built, compress: true);
    }

    static void Fill(StructValue texture, int width, int height, int format, byte[] data)
    {
        int images = texture.Has("m_ImageCount") ? Math.Max(1, texture.Int("m_ImageCount")) : 1;
        texture.Set("m_Width", width);
        texture.Set("m_Height", height);
        texture.Set("m_CompleteImageSize", data.Length / images);
        texture.Set("m_TextureFormat", format);
        texture.Set("m_MipCount", 1);
        texture.Set("m_MipMap", 0);
        texture.Set("m_MipsStripped", 0);
        texture["image data"] = new BytesValue(data);
        if (texture.Has("m_StreamData") && texture["m_StreamData"] is StructValue stream)
        {
            stream.Set("offset", 0);
            stream.Set("size", 0);
            stream["path"] = new TextValue("");
        }
    }
}
