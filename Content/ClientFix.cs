using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace OpenKogama.Content;

public static class ClientFix
{
    const int NothingToChange = 3;

    public static int Run(string[] args)
    {
        if (args is not [string input, string output])
        {
            Console.WriteLine("usage: client fix <Assembly-CSharp.dll> <output>");
            return 2;
        }
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
            using var module = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(input)), new ReaderParameters { AssemblyResolver = resolver });
            if (!Guard(module, "GodzillaLaser", "OnUnequip"))
            {
                Console.WriteLine("client fix: nothing to change");
                return NothingToChange;
            }
            var fixedAssembly = new MemoryStream();
            module.Write(fixedAssembly);
            byte[] bytes = fixedAssembly.ToArray();
            if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes))
            {
                File.WriteAllBytes(output + ".tmp", bytes);
                File.Move(output + ".tmp", output, overwrite: true);
            }
            Console.WriteLine("client fix: GodzillaLaser.OnUnequip no longer throws");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"client fix: {error.Message}");
            return 1;
        }
    }

    public static bool Guard(ModuleDefinition module, string typeName, string methodName)
    {
        if (module.GetType(typeName)?.Methods.FirstOrDefault(method => method.Name == methodName && !method.HasParameters && method.HasBody
            && method.ReturnType.MetadataType == MetadataType.Void) is not { } method)
            return false;
        MethodBody body = method.Body;
        if (body.ExceptionHandlers.Any(handler => handler.HandlerType == ExceptionHandlerType.Catch && handler.TryStart == body.Instructions[0]))
            return false;

        body.SimplifyMacros();
        Instruction start = body.Instructions[0];
        Instruction caught = Instruction.Create(OpCodes.Pop);
        Instruction end = Instruction.Create(OpCodes.Ret);
        foreach (Instruction instruction in body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret))
        {
            instruction.OpCode = OpCodes.Leave;
            instruction.Operand = end;
        }
        ILProcessor il = body.GetILProcessor();
        il.Append(caught);
        il.Append(Instruction.Create(OpCodes.Leave, end));
        il.Append(end);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = start,
            TryEnd = caught,
            HandlerStart = caught,
            HandlerEnd = end,
            CatchType = new TypeReference("System", "Exception", module, module.TypeSystem.CoreLibrary),
        });
        body.OptimizeMacros();
        return true;
    }
}
