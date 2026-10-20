using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Throws(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception(name); }
var settings = new SessionSettings(Stage: "up-and-down", Bots: 2, Wins: 5, RoundSeconds: 90, SuddenDeath: false);
var wire = settings.ToWireSettings();
Check(wire["round-seconds"] == "90" && wire["bots"] == "2" && wire["sudden-death"] == "off" && wire["ghosts"] == "on", "Kernel uses string-valued, hyphenated lobby settings");
Throws(() => new SessionSettings(Bots: 8).ToWireSettings(), "Reject excessive bots");
Throws(() => new SessionSettings(Stage: "invalid").ToWireSettings(), "Reject unknown stage");
Throws(() => new SessionSettings(BotDifficulty: "impossible").ToWireSettings(), "Reject unknown CPU difficulty");
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
Console.WriteLine($"PASS: {checks} client contract and session checks");
