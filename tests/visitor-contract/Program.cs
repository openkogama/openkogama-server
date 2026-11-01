using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;
using OpenKogama.Plugins;
using OpenKogama.World;
using OpenKogama.Handlers.Operations;
using OpenKogama.Mods.Patches;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args is ["--check-builder", var binaryPath, var metadataPath])
{
    byte[] original = File.ReadAllBytes(binaryPath);
    var image = new Il2Cpp((byte[])original.Clone(), File.ReadAllBytes(metadataPath));
    image.Load(Il2Cpp.Game, Il2Cpp.Engine);
    if (!BuilderToolFix.Apply(image, Console.WriteLine)) throw new Exception("native builder visibility fix did not apply");
    if (original.Where((value, index) => value != image.Binary[index]).Count() != 8)
        throw new Exception("native builder fix must change two visibility bytes and the six-byte name guard");
    if (BuilderToolFix.Apply(image, _ => { })) throw new Exception("native builder fix is not idempotent");
    Console.WriteLine("PASS native builder and pending-name fix on actual game binary: exactly eight bytes, idempotent");
    return 0;
}

if (args is ["--inspect-builder", var input, var metadata])
{
    var image = new Il2Cpp(File.ReadAllBytes(input), File.ReadAllBytes(metadata));
    image.Load(Il2Cpp.Game, Il2Cpp.Engine);
    foreach (var (type, method, count) in new[] { ("MVBuildModeAvatarRemote", "SetLaserPointerVisibility", 1), ("MVBuildModeAvatarRemote", "Activate", 3), ("MVBuildModeAvatarRemote", "Initialize", 0) })
        if (image.Find(type, method, count) is uint rva)
        {
            int offset = image.Offset(rva), length = Math.Min(256, (int)(image.End(rva) - rva));
            Console.WriteLine($"{type}.{method}: RVA={rva:X} RAW={offset:X} SIZE={image.End(rva)-rva} {Convert.ToHexString(image.Binary.AsSpan(offset, length))}");
        }
    Console.WriteLine($"SetActive RVA={image.FindEngine("UnityEngine.GameObject", "SetActive", 1):X}");
    return 0;
}

if (args is ["--seen"]) return Seasonal.Seen("visitor") && !Seasonal.TryMarkSeen("visitor") ? 0 : 1;
int checks = 0;
void Check(bool pass, string message)
{
    if (!pass) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
var tick = typeof(Session).GetMethod("TickVisitor", Private, [typeof(long), typeof(DateTime)])!;
void Tick(Session session, long now, DateTime? date = null) => tick.Invoke(session, [now, date ?? new DateTime(2026, 10, 31)]);
int Visitor(Session session) => (int)typeof(Session).GetField("_visitorAvatar", Private)!.GetValue(session)!;
Session NewSession(bool play = true)
{
    var world = new GameWorld();
    world.Add(new WorldObject { Id = world.NewObjectId(), Type = WorldObjectType.Group });
    return new Session(world, null, play: play);
}
var table = ProtocolTable.For("3.5.14");
using var server = new PhotonServer();
short nextPeer = 0;
Player AddPlayer(Session session, List<EventData> events, bool modern = true, List<OperationResponse>? responses = null)
{
    var peer = (PhotonPeer)Activator.CreateInstance(typeof(PhotonPeer), Private, null,
        [server, new IPEndPoint(IPAddress.Loopback, 12000 + nextPeer), ++nextPeer, 1], null)!;
    typeof(PhotonPeer).GetField("Sender", Private)!.SetValue(peer, (Action<byte[]>)(bytes =>
    {
        if (bytes[1] == EnetProtocol.MsgEvent) events.Add(Protocol16.ReadEventData(bytes, 2));
        else if (bytes[1] == EnetProtocol.MsgOperationResponse) responses?.Add(Protocol16.ReadOperationResponse(bytes, 2));
    }));
    if (modern) peer.Translator = new OperationRemap(table);
    var player = session.Add(peer, buildAvatar: false);
    player.InWorld = true;
    player.Mode = GameMode.Play;
    player.ClientVersion = modern ? "3.5.14" : ClientProtocols.ServerVersion;
    player.LastPosition = [0f, 2f, 0f];
    return player;
}
bool Is(EventData e, EventCode code) => e.Code == table.EventCodes[code.ToString()];
object? Value(EventData e, ParameterKey key) => e.Parameters[(byte)table.ParameterKeys[key.ToString()]];
string previousDirectory = Environment.CurrentDirectory;
string scratch = Path.Combine(Path.GetTempPath(), "openkogama-visitor-test-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);
try
{
    Environment.CurrentDirectory = scratch;
    using (var module = ModuleDefinition.CreateModule("BuilderVisibilityTest", ModuleKind.Dll))
    {
        var gameObject = new TypeDefinition("UnityEngine", "GameObject", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(gameObject);
        var setActive = new MethodDefinition("SetActive", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        setActive.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
        gameObject.Methods.Add(setActive);
        setActive.Body.GetILProcessor().Emit(OpCodes.Ret);
        var remote = new TypeDefinition("", "MVBuildModeAvatarRemote", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(remote);
        var visibility = new MethodDefinition("SetLaserPointerVisibility", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        visibility.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
        remote.Methods.Add(visibility);
        var il = visibility.Body.GetILProcessor();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, setActive);
        il.Emit(OpCodes.Ret);
        Check(BuilderToolFix.Apply(module) && visibility.Body.Instructions[1].OpCode == OpCodes.Ldarg_1,
            "Mono builder visibility honors its visibility argument");
        Check(!BuilderToolFix.Apply(module), "Mono builder visibility fix is idempotent");
        var container = new TypeDefinition("", "MVPlayerContainer", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(container);
        var contains = new MethodDefinition("ContainsKey", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Boolean);
        contains.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        container.Methods.Add(contains);
        contains.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0);
        contains.Body.GetILProcessor().Emit(OpCodes.Ret);
        var ui = new TypeDefinition("", "AvatarUIHandlerRemote", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(ui);
        var name = new MethodDefinition("UpdateNameTag", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        ui.Methods.Add(name);
        var names = name.Body.GetILProcessor();
        var update = Instruction.Create(OpCodes.Nop);
        names.Emit(OpCodes.Ldnull);
        names.Emit(OpCodes.Ldc_I4_0);
        names.Emit(OpCodes.Callvirt, contains);
        names.Emit(OpCodes.Brtrue_S, update);
        names.Emit(OpCodes.Ret);
        names.Append(update);
        names.Emit(OpCodes.Ret);
        Check(BuilderToolFix.Apply(module) && name.Body.Instructions[3].OpCode == OpCodes.Pop
            && name.Body.Instructions[4].OpCode == OpCodes.Br, "pending actor name is updated instead of keeping prefab text");
        Check(!BuilderToolFix.Apply(module), "pending-name fix is idempotent");
    }
    Check(!Seasonal.Halloween(new DateTime(2026, 10, 23)) && Seasonal.Halloween(new DateTime(2026, 10, 24))
        && Seasonal.Halloween(new DateTime(2026, 11, 2)) && !Seasonal.Halloween(new DateTime(2026, 11, 3)), "Halloween date boundaries");
    var api = (IServer)Activator.CreateInstance(typeof(PluginHost).GetNestedType("PluginServer", BindingFlags.NonPublic)!, true)!;
    api.SeasonalEventsEnabled = false;
    var session = NewSession();
    var events = new List<EventData>();
    var responses = new List<OperationResponse>();
    var player = AddPlayer(session, events, responses: responses);
    new GetAllPlanetPermissions(session).Handle(player.Peer, new OperationRequest { OperationCode = (byte)OperationCode.GetAllPlanetPermissions });
    using (var permissions = JsonDocument.Parse((string)responses[^1].Parameters[(byte)table.ParameterKeys["Data"]]!))
        Check(permissions.RootElement.GetProperty(Session.VisitorProfileId.ToString()).EnumerateArray().Any(item => item.GetInt32() == 18),
            "cached permissions contain visitor EditCube permission before its arrival");
    new GetPlanetOwnerships(session).Handle(player.Peer, new OperationRequest { OperationCode = (byte)OperationCode.GetPlanetOwnerships });
    using (var ownerships = JsonDocument.Parse((string)responses[^1].Parameters[(byte)table.ParameterKeys["Data"]]!))
        Check(ownerships.RootElement.GetProperty("planetOwnerships").EnumerateArray().Any(item =>
            item.GetProperty("profileID").GetInt32() == Session.VisitorProfileId && item.GetProperty("planetOwnership").GetInt32() == 1),
            "visitor has an editor entry in cached planet ownership data");
    Tick(session, 0);
    Tick(session, 3_600_000);
    Check(!Seasonal.Enabled && Visitor(session) < 0 && !Seasonal.Seen("visitor"), "plugin switch prevents and does not consume visit");
    api.SeasonalEventsEnabled = true;
    var edit = NewSession(play: false);
    var builder = AddPlayer(edit, []);
    Tick(edit, 0);
    Check(((Dictionary<Player, long>)typeof(Session).GetField("_playingSince", Private)!.GetValue(edit)!).ContainsKey(builder), "time in build mode counts toward the visitor too");
    Tick(session, 0, new DateTime(2026, 12, 1));
    Tick(session, 3_600_000, new DateTime(2026, 12, 1));
    Check(Visitor(session) < 0 && !Seasonal.Seen("visitor"), "out-of-season visit is not consumed");
    Tick(session, 0);
    player.LastPosition = [float.NaN, 6f, 0f];
    Tick(session, 3_599_999);
    Check(Visitor(session) < 0, "no visitor before one hour");
    player.LastPosition = [500f, 6f, 0f];
    Tick(session, 3_605_000);
    Check(Visitor(session) < 0, "invalid and distant positions are rejected");
    player.LastPosition = [20f, 6f, 0f];
    Tick(session, 3_610_000);
    Check(Visitor(session) < 0, "occupied position is rejected");
    player.LastPosition = [0f, 2f, 0f];
    Tick(session, 3_610_001);
    int avatar = Visitor(session);
    Check(avatar >= 0, "visitor appears after one hour at a recorded position");
    var root = session.World.Find(avatar)!;
    Check(root.Type == WorldObjectType.BuildModeAvatar && root.Position.SequenceEqual(new[] { 20f, 7.5f, 0f }), "builder avatar uses observed position and small hover");
    Check((float)root.Runtime.Single(pair => pair.Key == "pointRotationPitch").Value == 0f
        && (float)root.Runtime.Single(pair => pair.Key == "headRotationPitch").Value == 0f,
        "visitor does not force a pointing gesture or tilt its head");
    var tool = (List<(string Key, PackedType Type, object Value)>)root.Runtime.Single(pair => pair.Key == "currentItem").Value;
    Check((int)tool.Single(pair => pair.Key == "type").Value == (int)AvatarItemType.LaserPointer
        && (byte)tool.Single(pair => pair.Key == "st").Value == 0 && !(bool)tool.Single(pair => pair.Key == "fire").Value
        && (byte)tool.Single(pair => pair.Key == "cm").Value == 0,
        "visitor carries an idle builder cube with material zero and no laser beam");
    Check(session.Players.Count == 1 && session.Npcs.Count == 0, "visitor stays outside server player and NPC lists");
    var join = events.Single(e => Is(e, EventCode.Join));
    int actor = (int)Value(join, ParameterKey.ActorNr)!;
    Check((int)Value(join, ParameterKey.ProfileID)! == Session.VisitorProfileId, "visitor uses the preauthorized synthetic profile");
    Check((string)Value(join, ParameterKey.Username)! == " " && (bool)Value(join, ParameterKey.IsActorReady)! == false
        && JsonDocument.Parse((string)Value(join, ParameterKey.UserProfileData)!).RootElement.GetProperty("UserName").GetString() == " "
        && !events.Any(e => Is(e, EventCode.SetActorReady) || Is(e, EventCode.JoinNotification)), "anonymous pending actor has no ready event or join notification");
    Check(Is(events[0], EventCode.Join) && Is(events[1], EventCode.ReplicateSpawnRoleData)
        && events.Any(e => Is(e, EventCode.GetGameBatch)), "actor and active role precede avatar delivery");
    var lateEvents = new List<EventData>();
    var late = AddPlayer(session, lateEvents);
    session.IntroduceVisitor(late);
    session.IntroduceVisitor(late);
    Check(lateEvents.Count(e => Is(e, EventCode.Join)) == 1 && late.Saw(actor), "late joiner is introduced exactly once");
    Tick(session, 3_611_001);
    Check(events.Any(e => Is(e, EventCode.UpdateWorldObjectRunTimeData))
        && lateEvents.Any(e => Is(e, EventCode.UpdateWorldObjectRunTimeData)), "tool state reaches existing and late viewers after initialization");
    var oldEvents = new List<EventData>();
    var old = AddPlayer(session, oldEvents, modern: false);
    session.IntroduceVisitor(old);
    Check(oldEvents.Count == 0 && session.VisitorFor(old, session.World.ToSnapshot()).Objects.All(obj => obj.Id != avatar), "unsupported clients receive neither actor nor avatar");
    Check(WorldSerializer.Read(session.World.ToPlayData()).Objects.All(obj => obj.Id != avatar), "visitor is excluded from saved play state");
    Check(Seasonal.Seen("visitor") && File.Exists("seasonal.json") && !Seasonal.TryMarkSeen("visitor"), "persistent claim blocks another session");
    Tick(session, 3_670_000);
    Check(Visitor(session) == avatar, "visitor stays the full minute");
    Tick(session, 3_670_001);
    Check(Visitor(session) < 0 && session.World.Find(avatar) is null && events.Any(e => Is(e, EventCode.Leave))
        && lateEvents.Any(e => Is(e, EventCode.UnregisterWorldObject)), "avatar and pending actor leave all viewers after one minute");
    Tick(session, 99_000_000);
    Check(Visitor(session) < 0 && events.Count(e => Is(e, EventCode.Join)) == 1, "visitor never returns in the same process");
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = scratch, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--seen");
    using var child = Process.Start(start)!;
    child.WaitForExit();
    Check(child.ExitCode == 0, "fresh process remembers visit and refuses another claim");
    Console.WriteLine($"{checks} visitor checks passed");
    return 0;
}
finally
{
    Environment.CurrentDirectory = previousDirectory;
    Seasonal.Enabled = true;
    foreach (string file in Directory.GetFiles(scratch)) File.Delete(file);
    Directory.Delete(scratch);
}
