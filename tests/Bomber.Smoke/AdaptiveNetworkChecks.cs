using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;

internal static class AdaptiveNetworkChecks
{
    public static async Task Run(Uri server, bool expectPeer = false)
    {
        var clients = new List<NetworkSession>();
        var states = new Dictionary<string, JsonElement>();
        var proxies = new Dictionary<NetworkSession, ConnectionDropProxy>();
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
        try
        {
            var host = await Connect("Adaptive owner");
            Check(host.Party?.Adaptive == true, "ordinary private creation enters the adaptive room service");
            var guest = await Connect("Adaptive guest", host.JoinCode);
            var third = await Connect("Adaptive third", host.JoinCode);
            await PumpUntil(() => clients.All(c => c.Party?.CanStart == true), 10);
            // Real relay probe observations are collected before asking the room to choose authority.
            await PumpUntil(() => host.ConnectionQualitySamples >= 3 && guest.ConnectionQualitySamples >= 3 && third.ConnectionQualitySamples >= 3, 15);
            host.RequestStart();
            await PumpUntil(() => clients.All(c => c.Party?.Phase == "playing" && states.ContainsKey(c.PlayerId!)), 15);
            Check(clients.All(c => c.Party?.Hosting == (expectPeer ? "peer" : "cluster")), "every participant agrees on the selected authority kind");
            Check(clients.All(c => c.Party?.LeaderId == host.PlayerId), "host selection preserves the social room owner");
            Check(clients.All(c => states[c.PlayerId!].GetProperty("players").GetArrayLength() == 3), "selected authority runs the real shared three-player simulation");
            var authority = clients.FirstOrDefault(c => c.PlayerId == host.Party!.HostId);
            if (expectPeer)
            {
                Check(authority is not null, "relay fallback elects an actual connected player");
                var departed = clients.First(c => c != authority && c != host);
                int match = host.Party!.Match; int epoch = host.Party.Epoch;
                if (proxies.TryGetValue(departed, out var proxy))
                {
                    await PumpUntil(() => Ack(departed) >= 30, 10);
                    long oldSequence = Ack(departed); int connection = departed.ConnectionGeneration;
                    proxy.DropConnections();
                    await PumpUntil(() => departed.ConnectionGeneration > connection && departed.Status == "Connected", 10);
                    departed.SendInput(0, new Bomber.Game.PlayerInput(0, 1));
                    await PumpUntil(() => Ack(departed) is >= 1 && Ack(departed) < oldSequence, 10);
                    departed.ReleaseInputs();
                    Check(departed.Party?.Epoch == epoch && departed.Party.Match == match && departed.Party.Phase == "playing",
                        "actual socket reconnect accepts reset input sequences without restarting the peer match");
                }
                await departed.DisposeAsync(); clients.Remove(departed);
                await PumpUntil(() => clients.All(c => states[c.PlayerId!].GetProperty("players").GetArrayLength() == 2), 10);
                Check(clients.All(c => c.Party?.Phase == "playing" && c.Party.Match == match && c.Party.Epoch == epoch), "ordinary guest departure preserves the peer match and its generation");
                await authority!.DisposeAsync(); clients.Remove(authority);
                await PumpUntil(() => clients.All(c => c.Party?.Phase == "lobby"), 10);
                Check(clients.All(c => c.JoinCode == host.JoinCode || c.LobbyId == host.LobbyId), "host failure preserves the same logical room");
            }
            Console.WriteLine($"PASS {checks} adaptive network checks");
        }
        finally { foreach (var client in clients) await client.DisposeAsync(); foreach (var proxy in proxies.Values) await proxy.DisposeAsync(); }

        long Ack(NetworkSession client)
        {
            if (!states.TryGetValue(client.PlayerId!, out var state)) return -1;
            var player = state.GetProperty("players").EnumerateArray().FirstOrDefault(p => p.GetProperty("id").GetString() == client.PlayerId);
            return player.ValueKind == JsonValueKind.Object && player.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Number ? ack.GetInt64() : -1;
        }
        async Task<NetworkSession> Connect(string name, string? code = null)
        {
            ConnectionDropProxy? proxy = expectPeer && server.IsLoopback && server.Scheme == "http" ? new(server) : null;
            try
            {
                var client = await NetworkSession.ConnectAsync(proxy?.Origin ?? server, name, new SessionSettings(Bots: 0, Wins: 1), code is null ? NetworkMode.CreatePrivate : NetworkMode.JoinCode, code);
                clients.Add(client); if (proxy is not null) proxies[client] = proxy; return client;
            }
            catch { if (proxy is not null) await proxy.DisposeAsync(); throw; }
        }

        async Task PumpUntil(Func<bool> predicate, double seconds)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                foreach (var client in clients)
                {
                    client.Advance(1d / 60);
                    foreach (var frame in client.DrainMessages())
                        if (frame.GetProperty("t").GetString() == "state") states[client.PlayerId!] = frame.GetProperty("d").Clone();
                    if (client.IsEnded) throw new Exception(client.Status);
                }
                if (predicate()) return;
                await Task.Delay(10);
            } while (elapsed.Elapsed.TotalSeconds < seconds);
            throw new TimeoutException("Adaptive room condition timed out: " + string.Join("; ", clients.Select(c => $"{c.PlayerId} {c.Party} probes={c.ConnectionQualitySamples}")));
        }
    }
}
