using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OpenKogama.Content;

public static class ClientFix
{
    const byte Ret = 0x2A;
    const byte Nop = 0x00;
    const byte TinyHeader = 0x2;
    const byte MoreSections = 0x8;

    sealed record Silence(string Namespace, string Type, string Method, string Done);

    // stathat is gone, every post fails and 2.13 to 2.27 keep each one in their four download slots for a minute
    static readonly Silence[] Silenced = [new("StatHat", "Post/FormPoster", "PostForm", "stathat posts off")];

    public static int Run(string[] args)
    {
        if (args is not [string input, string output])
        {
            Console.WriteLine("usage: client fix <Assembly-CSharp.dll> <output>");
            return 2;
        }
        try
        {
            byte[] assembly = File.ReadAllBytes(input);
            List<string> fixes = Fix(assembly);
            File.WriteAllBytes(output + ".tmp", assembly);
            File.Move(output + ".tmp", output, overwrite: true);
            Console.WriteLine(fixes.Count == 0 ? "client fix: nothing to change" : $"client fix: {string.Join(", ", fixes)}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"client fix: {error.Message}");
            return 1;
        }
    }

    public static List<string> Fix(byte[] assembly)
    {
        var bodies = new List<(int Header, int Length, string Done)>();
        using (var pe = new PEReader(new MemoryStream(assembly, false)))
        {
            if (!pe.HasMetadata) throw new InvalidDataException("not a .net assembly");
            MetadataReader metadata = pe.GetMetadataReader();
            foreach (Silence silence in Silenced)
                if (Body(pe, metadata, silence) is (int header, int length))
                    bodies.Add((header, length, silence.Done));
        }

        var fixes = new List<string>();
        foreach ((int header, int length, string done) in bodies)
        {
            bool tiny = (assembly[header] & 3) == TinyHeader;
            Span<byte> code = assembly.AsSpan(header + (tiny ? 1 : (BinaryPrimitives.ReadUInt16LittleEndian(assembly.AsSpan(header)) >> 12) * 4), length);
            if (code[0] == Ret && !code[1..].ContainsAnyExcept(Nop) && (assembly[header] & MoreSections) == 0) continue;
            // the try blocks of the old body would be left around nops
            if (!tiny) assembly[header] &= unchecked((byte)~MoreSections);
            code[0] = Ret;
            code[1..].Fill(Nop);
            fixes.Add(done);
        }
        return fixes;
    }

    static (int Header, int Length)? Body(PEReader pe, MetadataReader metadata, Silence silence)
    {
        string[] path = silence.Type.Split('/');
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            if (!Named(metadata, type, silence.Namespace, path, path.Length - 1)) continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
                if (!metadata.StringComparer.Equals(method.Name, silence.Method) || method.RelativeVirtualAddress == 0 || !TakesNothing(metadata, method)) continue;
                int length = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader().Length;
                return length > 0 ? (FileOffset(pe, method.RelativeVirtualAddress), length) : null;
            }
        }
        return null;
    }

    static bool Named(MetadataReader metadata, TypeDefinition type, string ns, string[] path, int at)
    {
        if (!metadata.StringComparer.Equals(type.Name, path[at])) return false;
        TypeDefinitionHandle outer = type.GetDeclaringType();
        if (at == 0) return outer.IsNil && metadata.StringComparer.Equals(type.Namespace, ns);
        return !outer.IsNil && Named(metadata, metadata.GetTypeDefinition(outer), ns, path, at - 1);
    }

    static bool TakesNothing(MetadataReader metadata, MethodDefinition method)
    {
        BlobReader signature = metadata.GetBlobReader(method.Signature);
        SignatureHeader header = signature.ReadSignatureHeader();
        return header.IsInstance && !header.IsGeneric && signature.ReadCompressedInteger() == 0 && signature.ReadSignatureTypeCode() == SignatureTypeCode.Void;
    }

    static int FileOffset(PEReader pe, int rva)
    {
        foreach (SectionHeader section in pe.PEHeaders.SectionHeaders)
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData)
                return rva - section.VirtualAddress + section.PointerToRawData;
        throw new InvalidDataException("a method body lies outside the sections");
    }
}
