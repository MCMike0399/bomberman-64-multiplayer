using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

// All participants use real public sockets; the elected player runs the production simulation.
internal static class PeerLoadProbe
{
    public static async Task Run(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("--peer-capacity URL OUTPUT SECONDS [REGION] [STAGE]");
        var origin = new Uri(args[0]); string output = Path.GetFullPath(args[1]); int seconds = int.Parse(args[2]);
        string? region = args.ElementAtOrDefault(3); string stage = args.ElementAtOrDefault(4) ?? "top-rules";
        if (seconds is < 20 or > 600) throw new ArgumentException("Peer load must run for 20–600 seconds.");
        var settings = new SessionSettings(Stage: stage, Bots: 0, Wins: 5, RoundSeconds: 120)
        { Hosting = "peer", Advanced = new AdvancedRules { DropPercent = 100, BlockPercent = 100, ItemSeconds = 60, MaxBombs = 8, MaxFire = 5, EffectSeconds = 30 } };
        using var http = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); }; Console.CancelKeyPress += handler;
        var clients = new List<NetworkSession>();
        var samples = new Dictionary<string, List<Sample>>();
        var latest = new Dictionary<string, JsonElement>();
        var clock = Stopwatch.StartNew();
        string? allocator = null, room = null, code = null, server = null;
        bool allocatorReserved = false;
        Exception? failure = null; int maxBombs = 0, maxBlasts = 0;
        double started = 0;
        try
        {
            var guest = await Api("guests", new { displayName = "Peer capacity allocator" }); allocator = guest.GetProperty("token").GetString();
            var created = await Api("lobbies", new { game = "blast-room", visibility = "private", region, settings = settings.ToWireSettings() }, allocator);
            var lobby = created.GetProperty("lobby"); room = lobby.GetProperty("lobbyId").GetString(); code = lobby.GetProperty("joinCode").GetString(); server = lobby.GetProperty("serverId").GetString();
            allocatorReserved = true;
            if (region is not null && lobby.GetProperty("region").GetString() != region) throw new InvalidOperationException("Refusing to load a different region.");
            await Api($"lobbies/{room}/leave", new { }, allocator);
            var released = Stopwatch.StartNew();
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/realtime/v1/me"); request.Headers.Add("x-player-token", allocator);
                using var response = await http.SendAsync(request, stop.Token); response.EnsureSuccessStatusCode();
                using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop.Token));
                if (me.RootElement.GetProperty("lobbyId").ValueKind == JsonValueKind.Null) break;
                if (released.Elapsed.TotalSeconds > 10) throw new TimeoutException("Allocator reservation did not release.");
                await Task.Delay(20, stop.Token);
            }
            allocatorReserved = false;
            for (int i = 0; i < 8; i++)
            {
                var pending = NetworkSession.ConnectAsync(origin, "Peer load " + i, settings, NetworkMode.JoinCode, code, cancellationToken: stop.Token);
                while (!pending.IsCompleted) { Pump(); await Task.Delay(10, stop.Token); }
                var client = await pending; clients.Add(client); samples[client.PlayerId!] = [];
            }
            var ready = Stopwatch.StartNew();
            while (clients.Any(c => c.Party?.CanStart != true || c.ConnectionQualitySamples < 3))
            { if (ready.Elapsed.TotalSeconds > 30) throw new TimeoutException("Peer route qualification did not complete."); Pump(); await Task.Delay(10, stop.Token); }
            clients.Single(c => c.IsLeader).RequestStart();
            ready.Restart();
            while (clients.Any(c => c.Party?.Hosting != "peer" || !latest.ContainsKey(c.PlayerId!)))
            { if (ready.Elapsed.TotalSeconds > 30) throw new TimeoutException("No suitable bounded peer host was available."); Pump(); await Task.Delay(10, stop.Token); }
            started = clock.Elapsed.TotalSeconds;
            Console.WriteLine($"READY public eight-player forced-relay match on {server}");
            while (clock.Elapsed.TotalSeconds - started < seconds)
            { Pump(); await Task.Delay(10, stop.Token); }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var client in clients) await client.DisposeAsync();
            if (allocatorReserved && room is not null && allocator is not null)
                try { await Api($"lobbies/{room}/leave", new { }, allocator, cleanup: true); } catch (Exception error) { failure ??= error; }
            Console.CancelKeyPress -= handler;
        }
        var reports = samples.Select(pair =>
        {
            var steady = pair.Value.Where(s => s.At >= (started + 5) * 1000).ToArray();
            var gaps = steady.Zip(steady.Skip(1)).Where(p => p.First.Match == p.Second.Match).Select(p => p.Second.At - p.First.At).Order().ToArray();
            double Percentile(double p) => gaps.Length == 0 ? 0 : gaps[(int)Math.Ceiling((gaps.Length - 1) * p)];
            return new { snapshots = steady.Length, p95ArrivalMs = Percentile(.95), p99ArrivalMs = Percentile(.99), maxArrivalMs = Percentile(1), decodedBytes = steady.Sum(s => (long)s.Bytes) };
        }).ToArray();
        bool passed = failure is null && reports.Length == 8 && reports.All(r => r.snapshots >= (seconds - 5) * 5 && r.p99ArrivalMs <= 300) && maxBombs > 0;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            passed, seconds, region, stage, server, participants = 8, transport = "authenticated WSS relay", inputHz = 30,
            workload = "ordinary movement and bomb/carry/pump/throw/remote/stop/aim input, normal deaths and rematches; no inventory edits",
            maxBombs, maxBlasts, clients = reports, failure = failure?.Message,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} peer capacity probe: {output}");
        if (!passed) Environment.ExitCode = 1;

        void Pump()
        {
            long sequence = (long)(clock.Elapsed.TotalSeconds * 30);
            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                if (client.IsEnded) throw new IOException(client.Status);
                int direction = (int)(clock.Elapsed.TotalSeconds / 1.25 + i) % 4;
                client.SendInput(0, new PlayerInput(direction == 0 ? 1 : direction == 2 ? -1 : 0, direction == 1 ? 1 : direction == 3 ? -1 : 0,
                    A: sequence % 2 == 0, B: sequence % 180 is >= 120 and < 150, R: sequence % 15 == 0, Aim: sequence % 180 is >= 125 and < 145, Stop: sequence % 45 == 0));
                client.Advance(1d / 60);
                foreach (var frame in client.DrainMessages())
                    if (frame.GetProperty("t").GetString() == "state")
                    {
                        var state = frame.GetProperty("d"); latest[client.PlayerId!] = state.Clone();
                        maxBombs = Math.Max(maxBombs, state.GetProperty("bombs").GetArrayLength());
                        maxBlasts = Math.Max(maxBlasts, state.GetProperty("hazards").GetArrayLength());
                        samples[client.PlayerId!].Add(new(clock.Elapsed.TotalMilliseconds, client.Party?.Match ?? 0, JsonSerializer.SerializeToUtf8Bytes(frame).Length));
                    }
                if (client.IsLeader && client.Party?.CanStart == true && clients.Count == 8 && started > 0) client.RequestStart();
            }
        }
        async Task<JsonElement> Api(string path, object body, string? token = null, bool cleanup = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/realtime/v1/" + path) { Content = JsonContent.Create(body) };
            request.Headers.Add(OnlineProtocol.HeaderName, OnlineProtocol.WireRevision);
            if (token is not null) request.Headers.Add("x-player-token", token);
            using var response = await http.SendAsync(request, cleanup ? CancellationToken.None : stop.Token);
            response.EnsureSuccessStatusCode();
            return response.StatusCode == System.Net.HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
    private sealed record Sample(double At, int Match, int Bytes);
}
