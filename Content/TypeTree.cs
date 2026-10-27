using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.Content;

public sealed class TypeNode
{
    const int AlignBytes = 0x4000;
    const uint CommonString = 0x80000000;

    static readonly string[] CommonStrings =
    [
        "AABB", "AnimationClip", "AnimationCurve", "AnimationState", "Array", "Base", "BitField", "bitset", "bool", "char", "ColorRGBA",
        "Component", "data", "deque", "double", "dynamic_array", "FastPropertyName", "first", "float", "Font", "GameObject", "Generic Mono",
        "GradientNEW", "GUID", "GUIStyle", "int", "list", "long long", "map", "Matrix4x4f", "MdFour", "MonoBehaviour", "MonoScript",
        "m_ByteSize", "m_Curve", "m_EditorClassIdentifier", "m_EditorHideFlags", "m_Enabled", "m_ExtensionPtr", "m_GameObject", "m_Index",
        "m_IsArray", "m_IsStatic", "m_MetaFlag", "m_Name", "m_ObjectHideFlags", "m_PrefabInternal", "m_PrefabParentObject", "m_Script",
        "m_StaticEditorFlags", "m_Type", "m_Version", "Object", "pair", "PPtr<Component>", "PPtr<GameObject>", "PPtr<Material>",
        "PPtr<MonoBehaviour>", "PPtr<MonoScript>", "PPtr<Object>", "PPtr<Prefab>", "PPtr<Sprite>", "PPtr<TextAsset>", "PPtr<Texture>",
        "PPtr<Texture2D>", "PPtr<Transform>", "Prefab", "Quaternionf", "Rectf", "RectInt", "RectOffset", "second", "set", "short", "size",
        "SInt16", "SInt32", "SInt64", "SInt8", "staticvector", "string", "TextAsset", "TextMesh", "Texture", "Texture2D", "Transform",
        "TypelessData", "UInt16", "UInt32", "UInt64", "UInt8", "unsigned int", "unsigned long long", "unsigned short", "vector", "Vector2f",
        "Vector3f", "Vector4f", "m_ScriptingClassIdentifier", "Gradient", "Type*", "int2_storage", "int3_storage", "BoundsInt",
        "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "FileSize", "Hash128", "RenderingLayerMask", "fixed_array",
        "EntityId", "LoadableObjectId", "LoadableSceneId",
    ];

    static readonly Dictionary<uint, string> Common = BuildCommon();

    public string Type { get; private init; } = "";
    public string Name { get; private init; } = "";
    public int Size { get; private init; }
    public bool IsArray { get; private init; }
    public bool Aligned { get; private init; }
    public List<TypeNode> Children { get; } = [];

    public TypeNode? Child(string name) => Children.Find(child => child.Name == name);

    internal static TypeNode Read(ByteReader reader, uint version)
    {
        int count = reader.Int();
        int strings = reader.Int();
        var nodes = new (byte Level, bool Array, uint Type, uint Name, int Size, int Meta)[count];
        for (int i = 0; i < count; i++)
        {
            reader.UShort();
            byte level = reader.Byte();
            byte flags = reader.Byte();
            uint type = (uint)reader.Int();
            uint name = (uint)reader.Int();
            int size = reader.Int();
            reader.Int();
            int meta = reader.Int();
            if (version >= 19) reader.Skip(8);
            nodes[i] = (level, (flags & 1) != 0, type, name, size, meta);
        }
        byte[] buffer = reader.Take(strings).ToArray();

        var path = new List<TypeNode>();
        TypeNode? root = null;
        foreach ((byte level, bool array, uint type, uint name, int size, int meta) in nodes)
        {
            var node = new TypeNode
            {
                Type = Text(buffer, type),
                Name = Text(buffer, name),
                Size = size,
                IsArray = array,
                Aligned = (meta & AlignBytes) != 0,
            };
            if (level == 0) root = node;
            else path[level - 1].Children.Add(node);
            if (path.Count > level) path.RemoveRange(level, path.Count - level);
            path.Add(node);
        }
        return root ?? throw new InvalidDataException("empty type tree");
    }

    public TypeValue ReadValue(byte[] data)
    {
        var reader = new ByteReader(data);
        TypeValue value = ReadValue(reader);
        if (reader.Position != data.Length) throw new InvalidDataException($"{Type} read {reader.Position} of {data.Length} bytes");
        return value;
    }

    public byte[] WriteValue(TypeValue value)
    {
        using var output = new MemoryStream();
        WriteValue(output, value);
        return output.ToArray();
    }

    TypeValue ReadValue(ByteReader reader)
    {
        TypeValue value;
        if (Type == "string")
        {
            value = new TextValue(Encoding.UTF8.GetString(reader.Take(reader.Int())));
            reader.Align(4);
            return value;
        }
        if (Type == "TypelessData")
        {
            value = new BytesValue(reader.Take(reader.Int()).ToArray());
        }
        else if (Children is [{ IsArray: true } array])
        {
            int size = reader.Int();
            TypeNode element = array.Children[1];
            if (element.Children.Count == 0 && element.Size == 1)
                value = new BytesValue(reader.Take(size).ToArray());
            else
            {
                var items = new List<TypeValue>(size);
                for (int i = 0; i < size; i++) items.Add(element.ReadValue(reader));
                value = new ArrayValue(items);
            }
            if (array.Aligned) reader.Align(4);
        }
        else if (Children.Count == 0)
        {
            value = new RawValue(reader.Take(Size).ToArray());
        }
        else
        {
            var fields = new StructValue();
            foreach (TypeNode child in Children) fields.Fields.Add((child.Name, child.ReadValue(reader)));
            value = fields;
        }
        if (Aligned) reader.Align(4);
        return value;
    }

    void WriteValue(MemoryStream output, TypeValue value)
    {
        switch (value)
        {
            case TextValue text when Type == "string":
                byte[] bytes = Encoding.UTF8.GetBytes(text.Text);
                WriteInt(output, bytes.Length);
                output.Write(bytes);
                Align(output);
                return;
            case BytesValue raw when Type == "TypelessData":
                WriteInt(output, raw.Bytes.Length);
                output.Write(raw.Bytes);
                break;
            case BytesValue raw when Children is [{ IsArray: true } array]:
                WriteInt(output, raw.Bytes.Length);
                output.Write(raw.Bytes);
                if (array.Aligned) Align(output);
                break;
            case ArrayValue items when Children is [{ IsArray: true } array]:
                WriteInt(output, items.Items.Count);
                foreach (TypeValue item in items.Items) array.Children[1].WriteValue(output, item);
                if (array.Aligned) Align(output);
                break;
            case RawValue raw when Children.Count == 0:
                if (raw.Bytes.Length != Size) throw new InvalidDataException($"{Name} needs {Size} bytes, not {raw.Bytes.Length}");
                output.Write(raw.Bytes);
                break;
            case StructValue fields:
                foreach (TypeNode child in Children)
                    child.WriteValue(output, fields[child.Name]);
                break;
            default:
                throw new InvalidDataException($"{Name} ({Type}) cannot hold {value.GetType().Name}");
        }
        if (Aligned) Align(output);
    }

    static void WriteInt(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    static void Align(MemoryStream output)
    {
        while (output.Position % 4 != 0) output.WriteByte(0);
    }

    static string Text(byte[] buffer, uint offset)
    {
        if ((offset & CommonString) != 0) return Common.GetValueOrDefault(offset & ~CommonString, offset.ToString());
        int end = Array.IndexOf(buffer, (byte)0, (int)offset);
        return Encoding.UTF8.GetString(buffer, (int)offset, (end < 0 ? buffer.Length : end) - (int)offset);
    }

    static Dictionary<uint, string> BuildCommon()
    {
        var common = new Dictionary<uint, string>();
        uint offset = 0;
        foreach (string text in CommonStrings)
        {
            common[offset] = text;
            offset += (uint)Encoding.ASCII.GetByteCount(text) + 1;
        }
        return common;
    }
}

public abstract class TypeValue;

public sealed class RawValue(byte[] bytes) : TypeValue
{
    public byte[] Bytes { get; } = bytes;

    public static RawValue Of(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new RawValue(bytes);
    }

    public static RawValue Of(long value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return new RawValue(bytes);
    }

    public static RawValue Of(bool value) => new([value ? (byte)1 : (byte)0]);

    public int Int => Bytes.Length switch
    {
        1 => Bytes[0],
        2 => BinaryPrimitives.ReadInt16LittleEndian(Bytes),
        4 => BinaryPrimitives.ReadInt32LittleEndian(Bytes),
        _ => (int)BinaryPrimitives.ReadInt64LittleEndian(Bytes),
    };

    public RawValue With(long value)
    {
        byte[] bytes = new byte[Bytes.Length];
        switch (bytes.Length)
        {
            case 1: bytes[0] = (byte)value; break;
            case 2: BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)value); break;
            case 4: BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)value); break;
            default: BinaryPrimitives.WriteInt64LittleEndian(bytes, value); break;
        }
        return new RawValue(bytes);
    }
}

public sealed class TextValue(string text) : TypeValue
{
    public string Text { get; } = text;
}

public sealed class BytesValue(byte[] bytes) : TypeValue
{
    public byte[] Bytes { get; } = bytes;
}

public sealed class ArrayValue(List<TypeValue> items) : TypeValue
{
    public List<TypeValue> Items { get; } = items;
}

public sealed class StructValue : TypeValue
{
    public List<(string Name, TypeValue Value)> Fields { get; } = [];

    public bool Has(string name) => Fields.Exists(field => field.Name == name);

    public TypeValue this[string name]
    {
        get => Fields.Find(field => field.Name == name).Value ?? throw new KeyNotFoundException(name);
        set
        {
            int index = Fields.FindIndex(field => field.Name == name);
            if (index < 0) throw new KeyNotFoundException(name);
            Fields[index] = (name, value);
        }
    }

    public int Int(string name) => ((RawValue)this[name]).Int;

    public void Set(string name, long value)
    {
        if (Has(name)) this[name] = ((RawValue)this[name]).With(value);
    }
}
