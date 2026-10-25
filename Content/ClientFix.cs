using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace OpenKogama.Content;

public static class ClientFix
{
    const int NothingToChange = 3;
    const string ColossusLaser = "colossus-laser";
    const string WorldSwitch = "world-switch";
    const string Tcp = "tcp";
    const string NetworkGame = "MVNetworkGame";
    const int TcpProtocol = 1;
    const int MaxConstructorParameters = 4;
    static readonly byte?[] PeerCreation = [0x45, 0x33, 0xC9, 0x45, 0x33, 0xC0, 0x48, 0x8B, null, 0x48, 0x8B, 0xC8];
    static readonly byte[] SetTcpArgument = [0x41, 0xB0, TcpProtocol];
    const int ProtocolArgumentAt = 3;
    static readonly byte?[] PeerCreation32 = [Call, null, null, null, null, 0x6A, 0x00, 0x6A, 0x00, 0x8B, null, null, null, Call];
    static readonly byte[] PushTcp = [0x6A, TcpProtocol];
    const int ProtocolPushAt = 7;
    const string Controller = "MVGameControllerBase";
    const byte Call = 0xE8;
    const byte Jump = 0xE9;
    const byte Return = 0xC3;
    const int ReloadFrames = 60;
    const byte WindowMoverArguments = 7;
    const int CursorUnlocked = 0;
    const int PureDepth = 3;
    static readonly string[] Controllers = [Controller, "MVGameController"];

    public static int Run(string[] args)
    {
        if (args is not [string input, string output, .. string[] fixes])
        {
            Console.WriteLine($"usage: client fix <Assembly-CSharp.dll or GameAssembly.dll> <output> [{ColossusLaser}] [{WorldSwitch}] [{Tcp}]");
            return 2;
        }
        try
        {
            List<string> applied = Path.GetFileName(output).Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase)
                ? Native(input, output, fixes)
                : Managed(input, output, fixes.Length == 0 ? [ColossusLaser] : fixes);
            if (applied.Count == 0)
            {
                Console.WriteLine("client fix: nothing to change");
                return NothingToChange;
            }
            foreach (string fix in applied) Console.WriteLine($"client fix: applied {fix}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"client fix: {error.Message}");
            return 1;
        }
    }

    static List<string> Managed(string input, string output, string[] fixes)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
        using var module = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(input)), new ReaderParameters { AssemblyResolver = resolver });
        List<string> applied = [];
        if (fixes.Contains(ColossusLaser) && Guard(module, "GodzillaLaser", "OnUnequip")) applied.Add(ColossusLaser);
        if (fixes.Contains(WorldSwitch) && Reload(module)) applied.Add(WorldSwitch);
        if (fixes.Contains(Tcp) && UseTcp(module)) applied.Add(Tcp);
        if (applied.Count == 0) return applied;
        var fixedAssembly = new MemoryStream();
        module.Write(fixedAssembly);
        Save(output, fixedAssembly.ToArray());
        return applied;
    }

    static List<string> Native(string input, string output, string[] fixes)
    {
        if (!fixes.Contains(WorldSwitch) && !fixes.Contains(Tcp) || Metadata(output) is not string metadata) return [];
        Il2CppMethods methods;
        try
        {
            methods = new Il2CppMethods(File.ReadAllBytes(input), File.ReadAllBytes(metadata));
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException)
        {
            Console.WriteLine($"client fix: {error.Message}");
            return [];
        }
        List<string> applied = [];
        if (fixes.Contains(WorldSwitch) && Reload(methods)) applied.Add(WorldSwitch);
        if (fixes.Contains(Tcp) && UseTcp(methods)) applied.Add(Tcp);
        if (applied.Count > 0) Save(output, methods.Binary);
        return applied;
    }

    static bool UseTcp(ModuleDefinition module)
    {
        if (module.GetType(NetworkGame) is not { } game) return false;
        bool changed = false;
        foreach (MethodDefinition method in game.Methods.Where(method => method.HasBody))
        {
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction create in method.Body.Instructions.Where(PeerConstructor).ToList())
            {
                il.InsertBefore(create, il.Create(OpCodes.Pop));
                il.InsertBefore(create, il.Create(OpCodes.Ldc_I4, TcpProtocol));
                changed = true;
            }
        }
        return changed;
    }

    static bool PeerConstructor(Instruction instruction) =>
        instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference { Name: ".ctor", DeclaringType.Name: "PhotonPeer" } constructor
        && constructor.Parameters is [_, { ParameterType.Name: "ConnectionProtocol" }];

    static bool UseTcp(Il2CppMethods methods)
    {
        byte?[] pattern = methods.Is64Bit ? PeerCreation : PeerCreation32;
        byte[] image = methods.Binary;
        List<int> found = [];
        for (int parameters = 0; parameters <= MaxConstructorParameters; parameters++)
        {
            if (methods.Find(NetworkGame, ".ctor", parameters) is not uint constructor) continue;
            int start = methods.Offset(constructor), end = start + (int)(methods.End(constructor) - constructor);
            for (int at = start; at + pattern.Length <= end; at++)
                if (Matches(image, at, pattern)) found.Add(at);
        }
        if (found is not [int site])
        {
            Console.WriteLine($"client fix: {Tcp}: found the photon peer {found.Count} times, left as it is");
            return false;
        }
        if (methods.Is64Bit) SetTcpArgument.CopyTo(image, site + ProtocolArgumentAt);
        else PushTcp.CopyTo(image, site + ProtocolPushAt);
        return true;
    }

    static bool Matches(byte[] image, int at, byte?[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
            if (pattern[i] is byte expected && image[at + i] != expected) return false;
        return true;
    }

    static void Save(string output, byte[] bytes)
    {
        if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes)) return;
        File.WriteAllBytes(output + ".tmp", bytes);
        File.Move(output + ".tmp", output, overwrite: true);
    }

    static string? Metadata(string gameAssembly) =>
        Directory.GetDirectories(Path.GetDirectoryName(Path.GetFullPath(gameAssembly))!, "*_Data")
            .Select(folder => Path.Combine(folder, "il2cpp_data", "Metadata", "global-metadata.dat"))
            .FirstOrDefault(File.Exists);

    public static bool Reload(ModuleDefinition module)
    {
        if (Controllers.Select(name => module.GetType(name)).OfType<TypeDefinition>().FirstOrDefault() is not { } controller
            || controller.Methods.FirstOrDefault(method => method.Name == "TryReauth" && method.IsStatic && !method.HasParameters && method.HasBody) is not { } tryReauth
            || tryReauth.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference { Name: "WWWRequest" }) is not { } request)
            return false;

        var reload = new MethodDefinition("ReloadWorld", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        ILProcessor il = reload.Body.GetILProcessor();
        if (Engine(module, "UnityEngine.Cursor", "set_lockState", "UnityEngine.CursorLockMode") is { } lockState
            && Engine(module, "UnityEngine.Cursor", "set_visible", "System.Boolean") is { } visible)
        {
            il.Append(il.Create(OpCodes.Ldc_I4, CursorUnlocked));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(lockState)));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(visible)));
        }
        if (!Unload(module, il) && !Restart(module, controller, reload.Body)) return false;
        il.Append(il.Create(OpCodes.Ret));
        controller.Methods.Add(reload);

        ILProcessor reauth = tryReauth.Body.GetILProcessor();
        reauth.InsertBefore(request, reauth.Create(OpCodes.Pop));
        reauth.Replace(request, reauth.Create(OpCodes.Call, reload));
        KeepWindow(module, controller);
        if (module.GetType("HackingToolDetector")?.Methods.FirstOrDefault(method => method.Name == "Initialize" && method.IsStatic && method.HasBody
            && method.ReturnType.MetadataType == MetadataType.Void) is { } detector)
        {
            detector.Body.Instructions.Clear();
            detector.Body.ExceptionHandlers.Clear();
            detector.Body.Variables.Clear();
            detector.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        }
        return true;
    }

    static bool Unload(ModuleDefinition module, ILProcessor il)
    {
        if (module.GetType("GameLoader") is not { } loader
            || loader.Methods.FirstOrDefault(method => method.Name == "Cleanup" && !method.IsStatic && !method.HasParameters) is not { } cleanup
            || loader.Methods.FirstOrDefault(method => method.Name == "UnloadGame" && method.IsStatic && !method.HasParameters) is not { } unload)
            return false;

        cleanup.IsPrivate = false;
        cleanup.IsPublic = true;
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Call, cleanup));
        il.Append(il.Create(OpCodes.Call, unload));
        return true;
    }

    static bool Restart(ModuleDefinition module, TypeDefinition controller, MethodBody body)
    {
        if ((Engine(module, "UnityEngine.SceneManagement.SceneManager", "LoadScene", "System.Int32")
                ?? Engine(module, "UnityEngine.Application", "LoadLevel", "System.Int32")) is not { } load
            || Engine(module, "UnityEngine.Object", "FindObjectsOfType", "System.Type") is not { } find
            || Engine(module, "UnityEngine.Object", "FindObjectOfType", "System.Type") is not { } findOne
            || Engine(module, "UnityEngine.Object", "Destroy", "UnityEngine.Object") is not { } destroy
            || Engine(module, "UnityEngine.Object", "op_Implicit", "UnityEngine.Object") is not { } alive
            || Engine(module, "UnityEngine.Object", "op_Equality", "UnityEngine.Object", "UnityEngine.Object") is not { } same
            || Engine(module, "UnityEngine.Component", "get_gameObject") is not { } owner
            || Engine(module, "UnityEngine.Resources", "FindObjectsOfTypeAll", "System.Type") is not { } loaded
            || Engine(module, "UnityEngine.AssetBundle", "Unload", "System.Boolean") is not { } unload
            || find.DeclaringType.Module.GetType("UnityEngine.GameObject") is not { } gameObject
            || module.TypeSystem.Object.Resolve()?.Module.GetType("System.Type")?.Methods
                .FirstOrDefault(method => method.Name == "GetTypeFromHandle") is not { } typeOf
            || controller.Methods.FirstOrDefault(method => method.Name == "Update" && !method.IsStatic && !method.HasParameters && method.HasBody) is not { } update)
            return false;

        var pending = new FieldDefinition("reloadPending", FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.Boolean);
        var keep = new FieldDefinition("reloadKeeper", FieldAttributes.Private | FieldAttributes.Static, module.ImportReference(gameObject));
        controller.Fields.Add(pending);
        controller.Fields.Add(keep);
        HashSet<FieldDefinition> survivors = [pending, keep, .. controller.Fields.Where(field => field.IsStatic && field.Name == "disconnectIsOk")];
        ILProcessor il = body.GetILProcessor();
        if (module.GetType("UpdateController")?.Methods.FirstOrDefault(method => method.Name == "Clear" && method.IsStatic && !method.HasParameters) is { } clear)
            il.Append(il.Create(OpCodes.Call, clear));
        if (module.GetType("CullingApiWrapper")?.Methods.FirstOrDefault(method => method.Name == "Destroy" && method.IsStatic && !method.HasParameters) is { } culling)
            il.Append(il.Create(OpCodes.Call, culling));
        il.Append(il.Create(OpCodes.Ldtoken, controller));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(typeOf)));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(findOne)));
        il.Append(il.Create(OpCodes.Castclass, module.ImportReference(owner.DeclaringType)));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(owner)));
        il.Append(il.Create(OpCodes.Stsfld, keep));
        Each(module, body, module.ImportReference(find), module.ImportReference(gameObject), module.ImportReference(typeOf), (item, step) =>
        {
            il.Append(il.Create(OpCodes.Ldloc, item));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(alive)));
            il.Append(il.Create(OpCodes.Brfalse, step));
            il.Append(il.Create(OpCodes.Ldloc, item));
            il.Append(il.Create(OpCodes.Ldsfld, keep));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(same)));
            il.Append(il.Create(OpCodes.Brtrue, step));
            il.Append(il.Create(OpCodes.Ldloc, item));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(destroy)));
        });
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stsfld, pending));

        var later = new MethodDefinition("ReloadWorldLater", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        ILProcessor next = later.Body.GetILProcessor();
        next.Append(next.Create(OpCodes.Ldsfld, keep));
        next.Append(next.Create(OpCodes.Call, module.ImportReference(destroy)));
        Each(module, later.Body, module.ImportReference(loaded), module.ImportReference(unload.DeclaringType), module.ImportReference(typeOf), (item, step) =>
        {
            next.Append(next.Create(OpCodes.Ldloc, item));
            next.Append(next.Create(OpCodes.Castclass, module.ImportReference(unload.DeclaringType)));
            next.Append(next.Create(OpCodes.Ldc_I4_1));
            next.Append(next.Create(OpCodes.Callvirt, module.ImportReference(unload)));
        });
        TypeReference exception = module.ImportReference(module.TypeSystem.Object.Resolve().Module.GetType("System.Exception"));
        foreach (TypeDefinition type in module.Types.ToList())
            if (Fresh(module, type, survivors) is { } reset)
                Guarded(later.Body, reset, exception);
        next.Append(next.Create(OpCodes.Ldc_I4_0));
        next.Append(next.Create(OpCodes.Call, module.ImportReference(load)));
        next.Append(next.Create(OpCodes.Ret));
        controller.Methods.Add(later);

        ILProcessor frame = update.Body.GetILProcessor();
        Instruction start = update.Body.Instructions[0];
        frame.InsertBefore(start, frame.Create(OpCodes.Ldsfld, pending));
        frame.InsertBefore(start, frame.Create(OpCodes.Brfalse, start));
        frame.InsertBefore(start, frame.Create(OpCodes.Ldc_I4_0));
        frame.InsertBefore(start, frame.Create(OpCodes.Stsfld, pending));
        frame.InsertBefore(start, frame.Create(OpCodes.Call, later));
        frame.InsertBefore(start, frame.Create(OpCodes.Ret));
        if (controller.Methods.FirstOrDefault(method => method.Name == "LateUpdate" && !method.IsStatic && !method.HasParameters && method.HasBody) is { } late)
        {
            ILProcessor after = late.Body.GetILProcessor();
            Instruction first = late.Body.Instructions[0];
            after.InsertBefore(first, after.Create(OpCodes.Ldsfld, pending));
            after.InsertBefore(first, after.Create(OpCodes.Brfalse, first));
            after.InsertBefore(first, after.Create(OpCodes.Ret));
        }
        return true;
    }

    static void Each(ModuleDefinition module, MethodBody body, MethodReference find, TypeReference type, MethodReference typeOf, Action<VariableDefinition, Instruction> visit)
    {
        ILProcessor il = body.GetILProcessor();
        var items = new VariableDefinition(find.ReturnType);
        var item = new VariableDefinition(((ArrayType)find.ReturnType).ElementType);
        var index = new VariableDefinition(module.TypeSystem.Int32);
        body.Variables.Add(items);
        body.Variables.Add(item);
        body.Variables.Add(index);
        body.InitLocals = true;
        Instruction next = il.Create(OpCodes.Ldloc, items);
        Instruction step = il.Create(OpCodes.Ldloc, index);
        Instruction check = il.Create(OpCodes.Ldloc, index);
        il.Append(il.Create(OpCodes.Ldtoken, type));
        il.Append(il.Create(OpCodes.Call, typeOf));
        il.Append(il.Create(OpCodes.Call, find));
        il.Append(il.Create(OpCodes.Stloc, items));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, index));
        il.Append(il.Create(OpCodes.Br, check));
        il.Append(next);
        il.Append(il.Create(OpCodes.Ldloc, index));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Stloc, item));
        visit(item, step);
        il.Append(step);
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, index));
        il.Append(check);
        il.Append(il.Create(OpCodes.Ldloc, items));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Blt, next));
    }

    static MethodDefinition? Fresh(ModuleDefinition module, TypeDefinition type, HashSet<FieldDefinition> survivors)
    {
        if (type.HasGenericParameters || type.IsInterface || type.IsEnum || type.Name.StartsWith("<PrivateImplementationDetails>")) return null;
        List<MethodDefinition> nested = [.. type.NestedTypes.ToList().Select(inner => Fresh(module, inner, survivors)).OfType<MethodDefinition>()];
        List<FieldDefinition> fields = [.. type.Fields.Where(field => field.IsStatic && !field.IsLiteral && !survivors.Contains(field)
            && !field.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "ThreadStaticAttribute"))];
        if (fields.Count == 0 && nested.Count == 0) return null;

        Dictionary<FieldDefinition, List<Instruction>?> initial = Initializers(type, type.IsAbstract && type.IsSealed || UnityObject(type));
        var reset = new MethodDefinition("ResetStatics", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        ILProcessor il = reset.Body.GetILProcessor();
        foreach (FieldDefinition field in fields)
        {
            if (initial.TryGetValue(field, out List<Instruction>? setup))
            {
                if (setup is null) continue;
                field.IsInitOnly = false;
                foreach (Instruction instruction in setup)
                {
                    Instruction copy = il.Create(OpCodes.Nop);
                    copy.OpCode = instruction.OpCode;
                    copy.Operand = instruction.Operand;
                    il.Append(copy);
                }
                il.Append(il.Create(OpCodes.Stsfld, field));
                continue;
            }
            field.IsInitOnly = false;
            if (field.FieldType.IsValueType)
            {
                il.Append(il.Create(OpCodes.Ldsflda, field));
                il.Append(il.Create(OpCodes.Initobj, field.FieldType));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldnull));
                il.Append(il.Create(OpCodes.Stsfld, field));
            }
        }
        foreach (MethodDefinition inner in nested)
            il.Append(il.Create(OpCodes.Call, inner));
        il.Append(il.Create(OpCodes.Ret));
        type.Methods.Add(reset);
        return reset;
    }

    static Dictionary<FieldDefinition, List<Instruction>?> Initializers(TypeDefinition type, bool owned)
    {
        var initial = new Dictionary<FieldDefinition, List<Instruction>?>();
        if (type.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic && method.HasBody) is not { } constructor) return initial;
        IList<Instruction> code = constructor.Body.Instructions;
        for (int at = 1; at < code.Count; at++)
        {
            if (code[at].OpCode != OpCodes.Stsfld || (code[at].Operand as FieldReference)?.Resolve() is not { } field || field.DeclaringType != type) continue;
            initial[field] = initial.ContainsKey(field) ? null : Simple(code, at, owned);
        }
        return initial;
    }

    static List<Instruction>? Simple(IList<Instruction> code, int store, bool owned)
    {
        Instruction value = code[store - 1];
        if (Constant(value)) return [value];
        if (!owned || value.OpCode != OpCodes.Newobj || value.Operand is not MethodReference constructor || !Pure(constructor, 0)) return null;
        int arguments = constructor.Parameters.Count;
        if (store - 1 - arguments < 0) return null;
        List<Instruction> setup = [.. Enumerable.Range(store - 1 - arguments, arguments + 1).Select(index => code[index])];
        return setup.Take(arguments).All(Constant) ? setup : null;
    }

    static bool UnityObject(TypeDefinition type)
    {
        for (TypeReference? parent = type.BaseType; parent is not null;)
        {
            if (parent.FullName == "UnityEngine.Object") return true;
            try
            {
                parent = parent.Resolve()?.BaseType;
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }
        }
        return false;
    }

    static bool Pure(MethodReference constructor, int depth)
    {
        if (constructor.DeclaringType.Scope.Name is "mscorlib" or "System" or "System.Core") return true;
        if (depth > PureDepth || Definition(constructor) is not { HasBody: true } body) return false;
        foreach (Instruction instruction in body.Body.Instructions)
        {
            bool allowed = instruction.OpCode.Code switch
            {
                Code.Nop or Code.Ret or Code.Ldarg_0 or Code.Stfld => true,
                Code.Call or Code.Newobj => instruction.Operand is MethodReference { Name: ".ctor" } inner && Pure(inner, depth + 1),
                _ => Constant(instruction),
            };
            if (!allowed) return false;
        }
        return true;
    }

    static MethodDefinition? Definition(MethodReference method)
    {
        try
        {
            return method.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    static bool Constant(Instruction instruction) => instruction.OpCode.Code is Code.Ldnull or Code.Ldstr or Code.Ldftn
        or Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3
        or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8;

    static void Guarded(MethodBody body, MethodDefinition method, TypeReference exception)
    {
        ILProcessor il = body.GetILProcessor();
        Instruction call = il.Create(OpCodes.Call, method);
        Instruction caught = il.Create(OpCodes.Pop);
        Instruction after = il.Create(OpCodes.Nop);
        il.Append(call);
        il.Append(il.Create(OpCodes.Leave, after));
        il.Append(caught);
        il.Append(il.Create(OpCodes.Leave, after));
        il.Append(after);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = exception,
            TryStart = call,
            TryEnd = caught,
            HandlerStart = caught,
            HandlerEnd = after,
        });
    }

    static void KeepWindow(ModuleDefinition module, TypeDefinition controller)
    {
        if (Engine(module, "UnityEngine.Time", "get_frameCount") is not { } frames) return;
        MethodReference frameCount = module.ImportReference(frames);
        if ((module.GetType("MVGameControllerDesktop") ?? controller).Methods.FirstOrDefault(method => method.Name == "Start" && !method.HasParameters && method.HasBody) is { } start
            && start.Body.Instructions.FirstOrDefault(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                && instruction.Operand is MethodReference { Name: "InitStandAlone" }) is { } init)
            SkipOnReload(start, frameCount, init.Next);
        if (controller.Methods.FirstOrDefault(method => method.Name == "SetPosition" && method.IsStatic && method.Parameters.Count == 4
            && method.HasBody && method.ReturnType.MetadataType == MetadataType.Void) is { } position)
            SkipOnReload(position, frameCount, position.Body.Instructions[0]);
    }

    static void SkipOnReload(MethodDefinition method, MethodReference frameCount, Instruction from)
    {
        method.Body.SimplifyMacros();
        Instruction end = method.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(from, il.Create(OpCodes.Call, frameCount));
        il.InsertBefore(from, il.Create(OpCodes.Ldc_I4, ReloadFrames));
        il.InsertBefore(from, il.Create(OpCodes.Bgt, end));
        method.Body.OptimizeMacros();
    }

    static MethodDefinition? Engine(ModuleDefinition module, string typeName, string methodName, params string[] parameters)
    {
        foreach (AssemblyNameReference reference in module.AssemblyReferences)
        {
            AssemblyDefinition? assembly;
            try
            {
                assembly = module.AssemblyResolver.Resolve(reference);
            }
            catch (AssemblyResolutionException)
            {
                continue;
            }
            if (assembly?.MainModule.GetType(typeName)?.Methods.FirstOrDefault(method => method.Name == methodName
                && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)) is { } found)
                return found;
        }
        return null;
    }

    public static bool Reload(Il2CppMethods methods)
    {
        if (methods.Find(Controller, "TryReauth", 0) is not uint tryReauth
            || methods.Find("AsyncWWWManager", "WWWRequest", 1) is not uint request
            || methods.Find(Controller, "OnReceivedReAuthWebParametersFromHttpRequest", 1) is not uint cave
            || methods.Find("MVGameControllerDesktop", "get_Instance", 0) is not uint instance
            || methods.FindEngine("UnityEngine.Component", "get_gameObject", 0) is not uint gameObject
            || methods.FindEngine("UnityEngine.Object", "Destroy", 1) is not uint destroy
            || methods.Find("GameLoader", "Cleanup", 0) is not uint cleanup
            || methods.Find("GameLoader", "UnloadGame", 0) is not uint unload
            || methods.Shared(tryReauth) || methods.Shared(cave))
            return false;

        byte[] image = methods.Binary;
        int start = methods.Offset(tryReauth);
        List<int> sites = [];
        for (int at = start; at + 5 <= start + (methods.End(tryReauth) - tryReauth); at++)
            if (image[at] == Call && tryReauth + (at - start) + 5 + BitConverter.ToInt32(image, at + 1) == request) sites.Add(at);
        if (sites.Count != 1) return false;

        var code = new List<byte>();
        uint? windowMover = WindowMover(methods);
        if (windowMover is not null) code.AddRange(methods.Is64Bit ? [0x31, 0xC0, Return] : [0x31, 0xC0, 0xC2, WindowMoverArguments * 4, 0x00]);
        uint entry = cave + (uint)code.Count;
        if (methods.Is64Bit) code.AddRange([0x48, 0x83, 0xEC, 0x28]);
        if (windowMover is uint slot) Point(code, cave, methods.Is64Bit, slot, cave);
        (uint? Target, int? Argument)[] optional =
        [
            (methods.Find("UpdateController", "Clear", 0), 0),
            (methods.Find("AsyncWWWManager", "Reset", 0), 0),
            (methods.FindEngine("UnityEngine.Cursor", "set_lockState", 1), CursorUnlocked),
            (methods.FindEngine("UnityEngine.Cursor", "set_visible", 1), 1),
        ];
        (uint Target, int? Argument)[] calls =
        [
            (instance, 0),
            (gameObject, null),
            (destroy, null),
            (cleanup, 0),
            .. optional.Where(call => call.Target is not null).Select(call => (call.Target!.Value, call.Argument)),
            (unload, 0),
        ];
        foreach ((uint target, int? argument) in calls)
        {
            if (methods.Is64Bit) code.AddRange(argument switch { null => [0x48, 0x89, 0xC1], 0 => [0x31, 0xC9], int value => [0xB9, .. BitConverter.GetBytes(value)] });
            else code.AddRange(argument is int value ? [0x6A, 0x00, 0x6A, (byte)value] : [0x6A, 0x00, 0x50]);
            code.AddRange(methods.Is64Bit ? [0x31, 0xD2, Call] : [Call]);
            code.AddRange(BitConverter.GetBytes((int)(target - (cave + code.Count + 4))));
            if (!methods.Is64Bit) code.AddRange([0x83, 0xC4, 0x08]);
        }
        if (methods.Is64Bit) code.AddRange([0x48, 0x83, 0xC4, 0x28]);
        code.Add(Return);
        if (methods.End(cave) - cave < code.Count) return false;

        List<(int At, uint Site, uint Stub)> resizes = [];
        if (methods.FindEngine("UnityEngine.Time", "get_frameCount", 0) is uint frames)
        {
            var window = new List<byte>(code);
            foreach ((int at, uint call, uint target) in Resizes(methods))
            {
                uint stub = cave + (uint)window.Count;
                KeepWindow(window, cave, methods.Is64Bit, frames, target);
                resizes.Add((at, call, stub));
            }
            if (methods.End(cave) - cave >= window.Count) code = window;
            else resizes.Clear();
        }

        code.CopyTo(image, methods.Offset(cave));
        methods.Unrelocate(cave, code.Count);
        uint site = (uint)(tryReauth + (sites[0] - start));
        BitConverter.TryWriteBytes(image.AsSpan(sites[0] + 1), (int)(entry - (site + 5)));
        methods.Unrelocate(site + 1, 4);
        foreach ((int at, uint resize, uint stub) in resizes)
        {
            BitConverter.TryWriteBytes(image.AsSpan(at + 1), (int)(stub - (resize + 5)));
            methods.Unrelocate(resize + 1, 4);
        }
        if (methods.Find("HackingToolDetector", "Initialize", 1) is uint detector && !methods.Shared(detector))
        {
            image[methods.Offset(detector)] = Return;
            methods.Unrelocate(detector, 1);
        }
        return true;
    }

    static uint? WindowMover(Il2CppMethods methods)
    {
        if (methods.Find(Controller, "SetWindowPos", WindowMoverArguments) is not uint wrapper) return null;
        byte[] image = methods.Binary;
        byte[] store = methods.Is64Bit ? [0x48, 0x89, 0x05] : [0x83, 0xC4, 0x04, 0xA3];
        int from = methods.Offset(wrapper);
        List<uint> slots = [];
        for (int at = from; at + 9 + store.Length <= from + (methods.End(wrapper) - wrapper); at++)
        {
            if (image[at] != Call || !image.AsSpan(at + 5, store.Length).SequenceEqual(store)) continue;
            int operand = at + 5 + store.Length;
            slots.Add(methods.Is64Bit
                ? (uint)(wrapper + (operand + 4 - from) + BitConverter.ToInt32(image, operand))
                : (uint)(BitConverter.ToUInt32(image, operand) - methods.ImageBase));
        }
        return slots.Count == 1 ? slots[0] : null;
    }

    static void Point(List<byte> code, uint cave, bool wide, uint slot, uint target)
    {
        if (wide)
        {
            code.AddRange([0x48, 0x8D, 0x05]);
            code.AddRange(BitConverter.GetBytes((int)(target - (cave + code.Count + 4))));
            code.AddRange([0x48, 0x89, 0x05]);
            code.AddRange(BitConverter.GetBytes((int)(slot - (cave + code.Count + 4))));
            return;
        }
        code.AddRange([Call, 0, 0, 0, 0, 0x58]);
        uint here = cave + (uint)code.Count - 1;
        code.AddRange([0x8D, 0x88]);
        code.AddRange(BitConverter.GetBytes((int)(target - here)));
        code.AddRange([0x89, 0x88]);
        code.AddRange(BitConverter.GetBytes((int)(slot - here)));
    }

    static List<(int At, uint Site, uint Target)> Resizes(Il2CppMethods methods)
    {
        if (methods.Find("MVGameControllerDesktop", "Start", 0) is not uint start
            || methods.Find("FullScreenController", "set_FullScreen", 1) is not uint fullScreen)
            return [];
        byte[] image = methods.Binary;
        int from = methods.Offset(start);
        List<(int, uint, uint)> calls = [];
        for (int at = from; at + 5 <= from + (methods.End(start) - start) && calls.Count < 2; at++)
        {
            if (image[at] != Call) continue;
            uint site = (uint)(start + (at - from));
            uint target = (uint)(site + 5 + BitConverter.ToInt32(image, at + 1));
            if (target == fullScreen || calls.Count == 1 && methods.Known(target)) calls.Add((at, site, target));
        }
        return calls.Count == 2 ? calls : [];
    }

    static void KeepWindow(List<byte> code, uint cave, bool wide, uint frames, uint target)
    {
        void Relative(byte opcode, uint to)
        {
            code.Add(opcode);
            code.AddRange(BitConverter.GetBytes((int)(to - (cave + code.Count + 4))));
        }

        if (wide) code.AddRange([0x51, 0x52, 0x41, 0x50, 0x41, 0x51, 0x48, 0x83, 0xEC, 0x28, 0x31, 0xC9]);
        else code.AddRange([0x6A, 0x00]);
        Relative(Call, frames);
        if (wide) code.AddRange([0x48, 0x83, 0xC4, 0x28, 0x41, 0x59, 0x41, 0x58, 0x5A, 0x59]);
        else code.AddRange([0x83, 0xC4, 0x04]);
        code.Add(0x3D);
        code.AddRange(BitConverter.GetBytes(ReloadFrames));
        code.AddRange([0x7F, 0x05]);
        Relative(Jump, target);
        code.Add(Return);
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
