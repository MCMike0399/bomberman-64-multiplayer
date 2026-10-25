using Bomber.Cluster;
using Burbuja.Realtime.Abstractions;
using Burbuja.Realtime.Fleet;

if(args.FirstOrDefault()=="--fleet"){await AdaptiveFleetChecks.Run(args.Skip(1).ToArray());return;}

var strategy = new PreferredServerPlacement(["primary", "fallback"]);
var balancer = new LobbyBalancer([], strategy, allowCrossRegion: true);
var request = new PlacementRequest("blast", null, 8);
LobbyServerStatus Node(string id, int rooms = 0, LobbyServerState state = LobbyServerState.Active) =>
    new(id, id, state, rooms, 2, rooms * 8, "https://example.invalid/nodes/" + id);
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
Check(balancer.Choose([Node("fallback"), Node("primary", 1)], request) == "primary", "primary remains preferred while busier");
Check(balancer.Choose([Node("primary", 2), Node("fallback")], request) == "fallback", "full primary spills to fallback");
Check(balancer.Choose([Node("primary", 0, LobbyServerState.Draining), Node("fallback")], request) == "fallback", "draining primary receives no new rooms");
Check(balancer.Choose([Node("fallback")], request) == "fallback", "absent primary uses fallback");
Check(balancer.Choose([Node("fallback", 1), Node("primary")], request) == "primary", "recovered primary wins even while fallback has rooms");
Check(balancer.Choose([Node("primary", 2), Node("fallback", 2)], request) == null, "capacity is never exceeded");
Check(balancer.Choose([Node("unknown")], request) == null, "unconfigured nodes cannot receive rooms");
Check(balancer.Choose([Node("primary"), Node("fallback")], request with { Region = "fallback" }) == "fallback", "explicit capacity-test region is respected");
var adaptive = new PlacementRequest("blast-room", null, 8);
Check(balancer.Choose([
    Node("primary") with { AvailableResources = new Dictionary<string,int> { ["simulation"] = 0, ["peer-relay"] = 1 } },
    Node("fallback") with { AvailableResources = new Dictionary<string,int> { ["simulation"] = 1, ["peer-relay"] = 1 } }
], adaptive) == "fallback", "free fallback simulation beats an empty primary waiting-room slot");
foreach (string[] invalid in new[] { Array.Empty<string>(), new[] { "" }, new[] { "primary", "primary" } })
{
    try { _ = new PreferredServerPlacement(invalid); throw new Exception("Invalid order accepted"); }
    catch (ArgumentException) { Check(true, "invalid deployment order rejected"); }
}
var file = Path.Combine(Path.GetTempPath(), "blast-pressure-" + Guid.NewGuid().ToString("N") + ".json");
var now = DateTimeOffset.UtcNow;
var pressure = new PressureSnapshot(file, () => now);
var aware = new LobbyBalancer([], new PreferredServerPlacement(["primary", "fallback"], pressure), allowCrossRegion: true);
void Snapshot(double at, params string[] excluded) => File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { at, excludedServers = excluded }));
try
{
    Check(aware.Choose([Node("primary"), Node("fallback")], request) == "primary", "missing monitor preserves bounded baseline");
    Snapshot(now.ToUnixTimeMilliseconds()/1000.0, "primary");
    Check(aware.Choose([Node("primary"), Node("fallback")], request) == "fallback", "pressured primary moves new rooms to fallback");
    Check(aware.Choose([Node("primary"), Node("fallback")], request with { Region = "primary" }) == null, "pressure cannot be bypassed with a region");
    Snapshot(now.ToUnixTimeMilliseconds()/1000.0, "primary", "fallback");
    Check(aware.Choose([Node("primary"), Node("fallback")], request) == null, "both pressured hosts reject new rooms");
    Snapshot(now.AddSeconds(-91).ToUnixTimeMilliseconds()/1000.0, "primary");
    Check(aware.Choose([Node("primary"), Node("fallback")], request) == "primary", "stale monitor is not a new game availability dependency");
    File.WriteAllText(file, "{bad");
    Check(aware.Choose([Node("primary"), Node("fallback")], request) == "primary", "partial or corrupt observation preserves baseline");
    Snapshot(now.ToUnixTimeMilliseconds()/1000.0);
    Check(aware.Choose([Node("primary",0,LobbyServerState.Draining), Node("fallback")], request) == "fallback", "recovery never overrides an operator drain");
    Check(aware.Choose([Node("primary"), Node("fallback",1)], request) == "primary", "recovery restores primary preference without moving existing rooms");
}
finally { File.Delete(file); }
Console.WriteLine($"ALL CLUSTER PLACEMENT CHECKS PASSED ({checks})");
