using System.Text;

namespace OpenKogama.Content;

public sealed class Il2CppMethods
{
    const uint Sanity = 0xFAB11BAF;
    const string Game = "Assembly-CSharp.dll";
    const string Engine = "UnityEngine.CoreModule.dll";
    const int TypeSize = 88;
    const int ImageSize = 40;
    const int MaxMethods = 1_000_000;
    const int HighLow = 3;
    const int Dir64 = 10;

    readonly ulong _imageBase;
    readonly List<(uint Va, uint Size, uint Raw)> _sections = [];
    readonly Dictionary<(string Module, string Type, string Method, int Parameters), uint> _rvas = [];
    readonly uint[] _starts;
    readonly Dictionary<uint, int> _uses = [];

    public byte[] Binary { get; }
    public bool Is64Bit { get; }
    public ulong ImageBase => _imageBase;

    public Il2CppMethods(byte[] binary, byte[] metadata)
    {
        Binary = binary;
        int pe = BitConverter.ToInt32(binary, 0x3C);
        int sections = BitConverter.ToUInt16(binary, pe + 6);
        int optional = BitConverter.ToUInt16(binary, pe + 20);
        Is64Bit = BitConverter.ToUInt16(binary, pe + 24) == 0x20B;
        _imageBase = Is64Bit ? BitConverter.ToUInt64(binary, pe + 24 + 24) : BitConverter.ToUInt32(binary, pe + 24 + 28);
        for (int i = 0; i < sections; i++)
        {
            int at = pe + 24 + optional + i * 40;
            _sections.Add((BitConverter.ToUInt32(binary, at + 12), BitConverter.ToUInt32(binary, at + 16), BitConverter.ToUInt32(binary, at + 20)));
        }

        foreach (string module in new[] { Game, Engine })
        {
            (Dictionary<(string, string, int), uint> tokens, int count) = Tokens(metadata, module);
            ulong[] pointers = Pointers(module, count);
            foreach (((string type, string method, int parameters), uint token) in tokens)
            {
                ulong va = pointers[(token & 0xFFFFFF) - 1];
                if (va != 0) _rvas[(module, type, method, parameters)] = (uint)(va - _imageBase);
            }
            foreach (ulong va in pointers.Where(va => va != 0))
                _uses[(uint)(va - _imageBase)] = _uses.GetValueOrDefault((uint)(va - _imageBase)) + 1;
        }
        _starts = [.. _uses.Keys.Order()];
    }

    public uint? Find(string type, string method, int parameters) => Find(Game, type, method, parameters);

    public uint? FindEngine(string type, string method, int parameters) => Find(Engine, type, method, parameters);

    uint? Find(string module, string type, string method, int parameters) =>
        _rvas.TryGetValue((module, type, method, parameters), out uint rva) ? rva : null;

    public bool Shared(uint rva) => _uses.GetValueOrDefault(rva) > 1;

    public bool Known(uint rva) => _uses.ContainsKey(rva);

    public uint End(uint rva)
    {
        int next = Array.BinarySearch(_starts, rva + 1);
        return next >= 0 ? _starts[next] : ~next < _starts.Length ? _starts[~next] : rva;
    }

    public void Unrelocate(uint rva, int length)
    {
        int pe = BitConverter.ToInt32(Binary, 0x3C);
        int directory = pe + 24 + (Is64Bit ? 112 : 96) + 5 * 8;
        uint table = BitConverter.ToUInt32(Binary, directory), size = BitConverter.ToUInt32(Binary, directory + 4);
        if (table == 0 || size == 0) return;
        int start = Offset(table);
        for (int block = start; block + 8 <= start + size;)
        {
            uint page = BitConverter.ToUInt32(Binary, block);
            int blockSize = BitConverter.ToInt32(Binary, block + 4);
            if (blockSize < 8) return;
            for (int entry = block + 8; entry + 2 <= block + blockSize; entry += 2)
            {
                ushort value = BitConverter.ToUInt16(Binary, entry);
                int width = (value >> 12) switch { HighLow => 4, Dir64 => 8, _ => 0 };
                uint at = page + (uint)(value & 0xFFF);
                if (width > 0 && at < rva + length && at + width > rva)
                    BitConverter.TryWriteBytes(Binary.AsSpan(entry), (ushort)(value & 0xFFF));
            }
            block += blockSize;
        }
    }

    public int Offset(uint rva)
    {
        foreach ((uint va, uint size, uint raw) in _sections)
            if (rva >= va && rva < va + size) return (int)(rva - va + raw);
        throw new InvalidDataException($"rva {rva:X} is outside the file");
    }

    static (Dictionary<(string, string, int), uint> Tokens, int Count) Tokens(byte[] metadata, string module)
    {
        if (BitConverter.ToUInt32(metadata, 0) != Sanity) throw new InvalidDataException("not IL2CPP metadata");
        int version = BitConverter.ToInt32(metadata, 4);
        int methodSize = version switch { 29 => 32, 31 => 36, _ => throw new NotSupportedException($"IL2CPP metadata version {version}") };
        int Int(int at) => BitConverter.ToInt32(metadata, at);
        int strings = Int(24), methods = Int(48), types = Int(160), images = Int(168);
        if (Int(52) % methodSize != 0 || Int(164) % TypeSize != 0 || Int(172) % ImageSize != 0)
            throw new NotSupportedException($"IL2CPP metadata version {version} has an unknown layout");

        string Name(int index)
        {
            int start = strings + index;
            return Encoding.UTF8.GetString(metadata, start, Array.IndexOf(metadata, (byte)0, start) - start);
        }

        for (int image = images; image < images + Int(172); image += ImageSize)
        {
            if (Name(Int(image)) != module) continue;
            var tokens = new Dictionary<(string, string, int), uint>();
            var overloaded = new HashSet<(string, string, int)>();
            int count = 0;
            for (int type = Int(image + 8); type < Int(image + 8) + Int(image + 12); type++)
            {
                int at = types + type * TypeSize;
                string space = Name(Int(at + 4));
                string typeName = space.Length > 0 ? space + "." + Name(Int(at)) : Name(Int(at));
                bool nested = Int(at + 12) >= 0;
                for (int method = Int(at + 36); method < Int(at + 36) + BitConverter.ToUInt16(metadata, at + 64); method++)
                {
                    int definition = methods + method * methodSize;
                    uint token = BitConverter.ToUInt32(metadata, definition + methodSize - 12);
                    count = Math.Max(count, (int)(token & 0xFFFFFF));
                    var key = (typeName, Name(Int(definition)), (int)BitConverter.ToUInt16(metadata, definition + methodSize - 2));
                    if (!nested && !tokens.TryAdd(key, token)) overloaded.Add(key);
                }
            }
            foreach (var key in overloaded) tokens.Remove(key);
            return (tokens, count);
        }
        throw new InvalidDataException($"IL2CPP metadata has no {module}");
    }

    ulong[] Pointers(string module, int count)
    {
        byte[] name = Encoding.ASCII.GetBytes(module + "\0");
        var names = new HashSet<ulong>();
        for (int at = Binary.AsSpan().IndexOf(name); at >= 0; at = Next(Binary, name, at + 1))
            if (Rva(at) is uint rva) names.Add(_imageBase + rva);

        int size = Is64Bit ? 8 : 4;
        for (int at = 0; at + 3 * size <= Binary.Length; at += size)
        {
            if (!names.Contains(Pointer(at)) || (Is64Bit ? BitConverter.ToInt64(Binary, at + size) : BitConverter.ToInt32(Binary, at + size)) != count) continue;
            ulong table = Pointer(at + 2 * size);
            if (table < _imageBase || count is <= 0 or > MaxMethods) continue;
            int offset = Offset((uint)(table - _imageBase));
            if (offset + (long)count * size > Binary.Length) continue;
            return [.. Enumerable.Range(0, count).Select(i => Pointer(offset + i * size))];
        }
        throw new InvalidDataException($"GameAssembly.dll has no code module for {module} with {count} methods");
    }

    ulong Pointer(int at) => Is64Bit ? BitConverter.ToUInt64(Binary, at) : BitConverter.ToUInt32(Binary, at);

    uint? Rva(int offset)
    {
        foreach ((uint va, uint size, uint raw) in _sections)
            if (offset >= raw && offset < raw + size) return (uint)(offset - raw + va);
        return null;
    }

    static int Next(byte[] data, byte[] value, int from)
    {
        int found = data.AsSpan(from).IndexOf(value);
        return found < 0 ? -1 : from + found;
    }
}
