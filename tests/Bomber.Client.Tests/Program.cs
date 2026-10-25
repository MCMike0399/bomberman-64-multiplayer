using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

var checks = await UpdateChecks.Run();
checks += await ServerCompatibilityChecks.Run();
checks += PeerFrameChecks.Run();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Throws(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception(name); }
Check(new SessionSettings().Wins==2&&new MatchOptions().Wins==2&&new SessionSettings().RoundSeconds==120,"Defaults give first to two and one minute normal plus one minute sudden death");
Check(PlayerNames.Display("",3)=="P4"&&PlayerNames.Display("Player",2)=="P3"&&PlayerNames.Display(" Alex ",1)=="Alex","Names preserve chosen identity and default to the actual seat");
using(var stop=JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(0,0,Stop:true),1)))
    Check(stop.RootElement.GetProperty("d").GetProperty("stop").GetBoolean()&&!stop.RootElement.GetProperty("d").GetProperty("r").GetBoolean(),"Stop is independent from remote detonation on the wire");
Check(InputPolicy.Resolve(default,new PlayerInput(0,0,Stop:true)).Stop,"Device policy retains stop input");
var settings = new SessionSettings(Stage: "up-and-down", Bots: 2, Wins: 5, RoundSeconds: 90, SuddenDeath: false);
var wire = settings.ToWireSettings();
Check(wire["round-seconds"] == "90" && wire["bots"] == "2" && wire["sudden-death"] == "off" && wire["ghosts"] == "on", "Kernel uses string-valued, hyphenated lobby settings");
Throws(() => new SessionSettings(Bots: 8).ToWireSettings(), "Reject excessive bots");
Throws(() => new SessionSettings(Stage: "invalid").ToWireSettings(), "Reject unknown stage");
Throws(() => new SessionSettings(Stage: "circuit-garden").ToWireSettings(), "Tutorial arena cannot be offered to online or peer rooms");
Throws(() => new LocalSession(new SessionSettings(Stage:"circuit-garden")), "Training arena requires local practice");
await using(var tutorial=new LocalSession(new SessionSettings(Stage:"circuit-garden",Bots:0),practice:true,tutorialLesson:"move"))
{
    var state=tutorial.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state").GetProperty("d");
    Check(state.GetProperty("tutorial").GetProperty("id").GetString()=="move", "Local adapter carries validated tutorial identity into authority");
}
Throws(() => new SessionSettings(BotDifficulty: "impossible").ToWireSettings(), "Reject unknown CPU difficulty");
Check(new SessionSettings(BotDifficulty: "medium").ToWireSettings()["bot-difficulty"]=="medium", "Medium difficulty is accepted by client contracts");
Check(new SessionSettings(BotDifficulty: "normal").ToWireSettings()["bot-difficulty"]=="normal", "Legacy normal difficulty remains wire compatible");
Check(new SessionSettings(BotDifficulty: "hard").ToWireSettings()["bot-difficulty"]=="hard", "Selected difficulty reaches the kernel lobby settings");
Throws(() => new LocalSession(new SessionSettings(Bots: 0), 1), "Solo session needs a bot");
Throws(() => new LocalSession(new SessionSettings(Bots: 7), 2), "Enforce eight-player cap");
using (var input = JsonDocument.Parse(NetworkSession.EncodeInput(new PlayerInput(float.NaN, 9, true, true, false), 42)))
{
    var root = input.RootElement;
    Check(root.GetProperty("t").GetString() == "input" && root.GetProperty("s").GetInt64() == 42, "Input envelope includes type and sequence");
    var data = root.GetProperty("d");
    Check(data.GetProperty("x").GetSingle() == 0 && data.GetProperty("z").GetSingle() == 1, "Reject non-finite intent and clamp axes");
    Check(data.GetProperty("a").GetBoolean() && data.GetProperty("b").GetBoolean() && !data.GetProperty("r").GetBoolean(), "Input button shape");
}
var localUrl = NetworkSession.ResolveConnectUri(new Uri("http://127.0.0.1:18496"), "/realtime/v1/connect?token=abc%2Bdef");
Check(localUrl.Scheme == "ws" && localUrl.Port == 18496 && localUrl.Query == "?token=abc%2Bdef", "Relative socket reservation preserves port and token");
Check(NetworkSession.ResolveConnectUri(new Uri("https://example.test"), "wss://node.example.test:443/realtime/v1/connect?token=x").Host == "node.example.test", "Fleet socket connects to owning node");
foreach (string node in new[] { "pi", "divemac" })
{
    var fleetUrl = NetworkSession.ResolveConnectUri(new Uri(NetworkSession.DefaultServerUrl), $"{NetworkSession.DefaultServerUrl}/nodes/{node}/realtime/v1/connect?token=abc%2Bdef");
    Check(fleetUrl.Scheme == "wss" && fleetUrl.AbsolutePath == $"/nodes/{node}/realtime/v1/connect" && fleetUrl.Query == "?token=abc%2Bdef", "Public fleet reservation preserves owning node route and token");
}
try { NetworkSession.ResolveConnectUri(new Uri("https://example.test"), "ws://node.example.test/connect"); throw new Exception("downgrade accepted"); } catch (InvalidDataException) { checks++; }
await using (var local = new LocalSession(new SessionSettings(Bots: 0), 8))
{
    var initial = local.DrainMessages();
    Check(initial.Any(m => m.GetProperty("t").GetString() == "stage"), "Stage available before first tick");
    var state = initial.Last(m => m.GetProperty("t").GetString() == "state").GetProperty("d");
    Check(state.GetProperty("players").GetArrayLength() == 8, "Eight human local match");
    for (var i = 0; i < 140; i++) local.Advance(1d / 30);
    local.SendInput(0, new PlayerInput(0, 0, true));
    local.SendInput(0, default); // A tap entirely between simulation ticks must survive.
    for (var i = 0; i < 4; i++) local.Advance(1d / 30);
    var frames = local.DrainMessages();
    var snapshots = frames.Where(m => m.GetProperty("t").GetString() == "state").ToArray();
    Check(snapshots.Length == 1, "Slow consumer receives coalesced latest snapshot");
    Check(snapshots[0].GetProperty("d").GetProperty("bombs").GetArrayLength() == 1, "Local input creates an actual simulation bomb");
    Throws(() => local.SendInput(8, default), "Controller slot bounds");
    local.Advance(double.PositiveInfinity);
    local.Advance(-1);
    Check(local.DrainMessages().Count == 0, "Invalid time deltas do not advance");
    await local.DisposeAsync();
    await local.DisposeAsync();
    Check(local.IsEnded, "Disposal is idempotent and ends session");
}
Check(LocalControllerLayout.Capacity(6)==8 && LocalControllerLayout.Capacity(0)==2, "Two keyboards combine with six pads");
Check(LocalControllerLayout.GamepadIndex(0,8,6)==null && LocalControllerLayout.GamepadIndex(1,8,6)==null, "Eight-player game reserves first two seats for keyboards");
Check(Enumerable.Range(2,6).Select(i=>LocalControllerLayout.GamepadIndex(i,8,6)).SequenceEqual(Enumerable.Range(0,6).Select(i=>(int?)i)), "Six remaining seats each own a distinct pad");
Check(LocalControllerLayout.GamepadIndex(0,1,1)==0 && LocalControllerLayout.GamepadIndex(1,2,1)==null, "Small matches allow alternative pad controls without duplication");
using(var lobby=JsonDocument.Parse("""{"state":"countdown","countdownEndsAt":1792440000000,"players":[{"playerId":"p1","displayName":"Guest","slot":0,"connected":true}]}"""))
{
    var status=LobbyStatus.Parse(lobby.RootElement);
    Check(status.Players.Count==1 && status.Players[0].Name=="Guest" && status.Players[0].Connected, "WebSocket lobby roster uses players rather than HTTP members");
    Check(status.CountdownEndsAt==DateTimeOffset.FromUnixTimeMilliseconds(1792440000000), "WebSocket countdown uses Unix milliseconds");
}
using(var lobby=JsonDocument.Parse("""{"state":"open","joinCode":"ABC123","countdownEndsAt":null,"members":[]}"""))
    Check(LobbyStatus.Parse(lobby.RootElement).JoinCode=="ABC123", "HTTP lobby payload keeps shareable code");
await using(var practice=new LocalSession(new SessionSettings(Bots:0),1,practice:true))
{
    practice.DrainMessages();for(int i=0;i<300;i++)practice.Advance(1d/30);
    var state=practice.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state").GetProperty("d");
    Check(state.GetProperty("players").GetArrayLength()==1 && !practice.IsEnded, "Practice supports one human without ending the match");
}
var roster=new LocalRoster();
for(int slot=0;slot<8;slot++)roster.Set(slot,RosterRole.Off,slot);
roster.Set(0,RosterRole.Cpu,2);roster.Set(4,RosterRole.Human,7);roster.Set(7,RosterRole.Human,6);
Check(roster.AuthorityAppearances().SequenceEqual(new[]{7,6,2}), "Mixed roster packs humans first and CPU last while keeping each selected suit");
Check(roster.Binding(4,0).StartsWith("WASD")&&roster.Binding(7,0).StartsWith("ARROWS")&&roster.Binding(0,0)=="CPU CONTROLLED", "Gapped UI slots show actual human controller seats rather than their card number");
Check(roster.ValidationError(0)==null, "Two human keyboard seats and a CPU form a valid gapped roster");
roster.Set(6,RosterRole.Human,3);
Check(roster.ValidationError(0)!=null&&roster.ValidationError(1)==null&&roster.Binding(7,1)=="GAMEPAD 1", "Additional gapped humans require distinct connected gamepads");
Throws(()=>roster.Set(8,RosterRole.Human,0), "Roster rejects out-of-range UI slot");
Throws(()=>roster.Set(0,RosterRole.Human,8), "Roster rejects undefined appearance");
Throws(()=>NetworkSession.EncodeAppearance(-1), "Network appearance encoder rejects invalid index");
using(var appearance=JsonDocument.Parse(NetworkSession.EncodeAppearance(6)))
    Check(appearance.RootElement.GetProperty("t").GetString()=="appearance"&&appearance.RootElement.GetProperty("d").GetProperty("index").GetInt32()==6&&appearance.RootElement.GetProperty("d").EnumerateObject().Count()==1, "Cosmetic packet carries only the sender's chosen appearance and no target identity");
await using(var selected=new LocalSession(new SessionSettings(Bots:2),1,playerAppearances:new[]{7,3,1}))
{
    var players=selected.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state").GetProperty("d").GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).ToArray();
    Check(players.Select(p=>p.GetProperty("appearance").GetInt32()).SequenceEqual(new[]{7,3,1}), "Local adapter applies the actual human and CPU appearance selections to authoritative state");
}
ControllerChecks.Run(Check,Throws);
await PresentationChecks.Run(Check);
await AimInputChecks.Run(Check);
Console.WriteLine($"PASS: {checks} client contract and session checks");
