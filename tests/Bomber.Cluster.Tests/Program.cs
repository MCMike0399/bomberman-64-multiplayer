using Bomber.Cluster;
using Burbuja.Realtime.Abstractions;
using Burbuja.Realtime.Fleet;

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
foreach (string[] invalid in new[] { Array.Empty<string>(), new[] { "" }, new[] { "primary", "primary" } })
{
    try { _ = new PreferredServerPlacement(invalid); throw new Exception("Invalid order accepted"); }
    catch (ArgumentException) { Check(true, "invalid deployment order rejected"); }
}
Console.WriteLine($"ALL CLUSTER PLACEMENT CHECKS PASSED ({checks})");
