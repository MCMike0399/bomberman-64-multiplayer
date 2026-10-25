using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

internal static class AdaptiveFleetChecks
{
    private const string OperatorKey = "local-infrastructure-fixture-not-a-production-credential";
    private sealed record Room(NetworkSession Owner, NetworkSession Guest);

    public static async Task Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("--fleet SERVER_DLL PLUGINS_DIRECTORY OUTPUT_DIRECTORY");
        string dll = Path.GetFullPath(args[0]), plugins = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = deadline.Token;
        string nonce = Guid.NewGuid().ToString("N"), container = "blast-infra-" + nonce;
        var clients = new List<NetworkSession>();
        var ticks = new Dictionary<NetworkSession, long>();
        var placements = new Dictionary<NetworkSession, string>();
        var checks = new List<string>();
        Node? primary = null, fallback = null;
        string advice = Path.Combine(output, "placement.json");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        http.DefaultRequestHeaders.Add("x-api-key", OperatorKey);
        http.DefaultRequestHeaders.Add(OnlineProtocol.HeaderName, OnlineProtocol.WireRevision);
        string? failure = null;
        bool containerStarted = false;
        try
        {
            await Command("docker", ["run", "-d", "--rm", "--name", container, "--label", "blast.qa.run=" + nonce,
                "--cpus", "0.25", "--memory", "64m", "-p", "127.0.0.1::6379", "valkey/valkey:8-alpine",
                "valkey-server", "--maxmemory", "32mb", "--maxmemory-policy", "noeviction"], ct);
            containerStarted = true;
            string port = (await Command("docker", ["port", container, "6379/tcp"], ct)).Trim().Split(':').Last();
            Advice([]);
            primary = new Node("primary", 2, dll, plugins, output, advice, "127.0.0.1:" + port, nonce);
            fallback = new Node("fallback", 1, dll, plugins, output, advice, "127.0.0.1:" + port, nonce);
            await primary.Start(ct); await fallback.Start(ct);
            await Until(async () => (await Fleet(primary.Origin)).GetProperty("value").GetArrayLength() == 2, "two independent worker processes share the real Valkey directory");
            var first = await Pair(primary.Origin, "First", "server", true);
            var second = await Pair(primary.Origin, "Second", "server", true);
            var overflow = await Pair(primary.Origin, "Overflow", "server", true);
            Check(placements[first.Owner] == "primary" && placements[second.Owner] == "primary" && placements[overflow.Owner] == "fallback",
                "two primary simulations spill to fallback despite unused primary room slots");
            var waiting = await Pair(primary.Origin, "Waiting", "server", false);
            waiting.Owner.RequestStart(); await PumpFor(1);
            Check(waiting.Owner.Party?.Phase == "lobby" && !ticks.ContainsKey(waiting.Owner), "room admission succeeds independently while all simulation permits are occupied");
            Check(Used(await Fleet(primary.Origin), "primary", "simulation") == 2 && Used(await Fleet(primary.Origin), "fallback", "simulation") == 1,
                "operator counters enforce two primary and one fallback active simulations");
            string waitingCode = waiting.Owner.JoinCode!, waitingId = waiting.Owner.LobbyId!;
            await Leave(first);
            await Wait(() => ticks.ContainsKey(waiting.Owner), "queued start reuses released simulation capacity");
            Check(waiting.Owner.JoinCode == waitingCode && waiting.Owner.LobbyId == waitingId, "queued match keeps its original room code and membership");

            Advice(["primary"]); await PumpFor(1.5);
            var pressure = await Pair(primary.Origin, "Pressure", "server", false);
            Check(placements[pressure.Owner] == "fallback", "pressure excludes new primary rooms without changing manual drains");
            long runningTick = ticks[second.Owner];
            await Wait(() => ticks[second.Owner] > runningTick + 10, "existing primary simulation survives pressure exclusion");
            pressure.Owner.RequestStart(); await PumpFor(.8);
            Check(pressure.Owner.Party?.Phase == "lobby", "a full fallback refuses an extra active simulation");
            await Leave(overflow);
            await Wait(() => ticks.ContainsKey(pressure.Owner), "fallback queued start resumes after its own permit is released");
            Advice([]); await PumpFor(1.5);
            var peer = await Pair(primary.Origin, "Relay", "peer", false);
            await Wait(() => peer.Owner.ConnectionQualitySamples >= 3 && peer.Guest.ConnectionQualitySamples >= 3, "peer qualification measures actual authenticated relay paths");
            peer.Owner.RequestStart();
            await Wait(() => peer.Owner.Party?.Hosting == "peer" && ticks.ContainsKey(peer.Owner), "player execution adds capacity without consuming a simulation permit");
            Check(Used(await Fleet(primary.Origin), "primary", "simulation") == 2 && Used(await Fleet(primary.Origin), "primary", "peer-relay") == 1,
                "dedicated and peer fallback budgets remain separate");

            runningTick = ticks[pressure.Owner];
            await primary.Stop();
            await Until(async () => (await Fleet(fallback.Origin)).GetProperty("value").EnumerateArray().All(s => s.GetProperty("nodeId").GetString() != "primary"),
                "crashed primary expires from the directory");
            await Wait(() => ticks[pressure.Owner] > runningTick + 10, "fallback match continues after primary process loss");
            var failover = await Pair(fallback.Origin, "Failover", "peer", false);
            Check(placements[failover.Owner] == "fallback", "new rooms remain reachable through the surviving worker");
            await Wait(() => failover.Owner.ConnectionQualitySamples >= 3 && failover.Guest.ConnectionQualitySamples >= 3, "surviving worker qualifies new relay participants");
            failover.Owner.RequestStart();
            await Wait(() => ticks.ContainsKey(failover.Owner), "surviving worker starts an additional bounded player-hosted match");
            await primary.Start(ct);
            await Until(async () => (await Fleet(fallback.Origin)).GetProperty("value").GetArrayLength() == 2, "replacement primary registers with a new process generation");
            var recovered = await Pair(fallback.Origin, "Recovered", "server", true);
            Check(placements[recovered.Owner] == "primary", "recovered primary accepts new execution through the fallback API");
            runningTick = ticks[pressure.Owner];
            await Wait(() => ticks[pressure.Owner] > runningTick + 10, "primary recovery does not move the surviving fallback match");

            using var guestResponse = await http.PostAsJsonAsync(new Uri(primary.Origin, "/realtime/v1/guests"), new { displayName = "Unsupported mode probe" }, ct);
            using var guest = JsonDocument.Parse(await guestResponse.Content.ReadAsStringAsync(ct));
            using var legacyRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(primary.Origin, "/realtime/v1/lobbies"));
            legacyRequest.Headers.Add("x-player-token", guest.RootElement.GetProperty("token").GetString());
            // An API key would be a different identity, so this guest request uses its own client.
            using var guestHttp = new HttpClient(); guestHttp.DefaultRequestHeaders.Add(OnlineProtocol.HeaderName, OnlineProtocol.WireRevision);
            legacyRequest.Content = JsonContent.Create(new { game = "blast-peer", visibility = "private", settings = new { bots = "0" } });
            using var refused = await guestHttp.SendAsync(legacyRequest, ct);
            Check(refused.StatusCode == HttpStatusCode.ServiceUnavailable, "legacy peer mode cannot bypass the adaptive resource pools");
            var availableBefore = await Fleet(primary.Origin);
            int rooms = availableBefore.GetProperty("value").EnumerateArray().Sum(s => s.GetProperty("lobbies").GetInt32());
            for (int i = rooms; i < 16; i++)
            {
                var idle = await NetworkSession.ConnectAsync(primary.Origin, "Waiting seat", new SessionSettings(Bots: 0) { Hosting = "server" }, cancellationToken: ct);
                clients.Add(idle); Pump();
            }
            bool full = false;
            try
            {
                var unexpected = await NetworkSession.ConnectAsync(primary.Origin, "Excess seat", new SessionSettings(Bots: 0) { Hosting = "server" }, cancellationToken: ct);
                clients.Add(unexpected);
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.ServiceUnavailable) { full = true; }
            Check(full, "room coordination remains bounded independently of execution capacity");
            Check(Used(await Fleet(primary.Origin), "primary", "simulation") == 1, "quiet waiting rooms do not reserve additional simulations");
            Console.WriteLine($"PASS {checks.Count} real fleet infrastructure checks");
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            await Task.WhenAll(clients.Select(async c => { try { await c.DisposeAsync(); } catch { } }));
            if (primary is not null) await primary.Stop();
            if (fallback is not null) await fallback.Stop();
            try
            {
                string owner = (await Command("docker", ["inspect", "--format", "{{index .Config.Labels \"blast.qa.run\"}}", container], CancellationToken.None)).Trim();
                if (owner == nonce) await Command("docker", ["stop", container], CancellationToken.None);
                else if (containerStarted) failure ??= "Container ownership changed; cleanup refused.";
            }
            catch (Exception error) { if (containerStarted) failure ??= "Owned container cleanup failed: " + error.Message; }
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { passed = failure is null, checks, failure, serverSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dll))).ToLowerInvariant(), protocol = OnlineProtocol.Revision }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failure is not null) throw new IOException(failure);

        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
        void Advice(string[] excluded)
        {
            string temp = advice + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d, excludedServers = excluded }));
            File.Move(temp, advice, true);
        }
        void Pump()
        {
            foreach (var client in clients)
            {
                if (client.IsEnded) continue;
                client.Advance(1d / 60);
                foreach (var frame in client.DrainMessages())
                {
                    string? type = frame.GetProperty("t").GetString();
                    var data = frame.GetProperty("d");
                    if (type == "state") ticks[client] = frame.GetProperty("k").GetInt64();
                    if (type == "sys.welcome") data = data.GetProperty("lobby");
                    if (type is "sys.lobby" or "sys.welcome" && data.TryGetProperty("serverId", out var id)) placements[client] = id.GetString()!;
                }
            }
        }
        async Task PumpFor(double seconds)
        { var watch = Stopwatch.StartNew(); while (watch.Elapsed.TotalSeconds < seconds) { ct.ThrowIfCancellationRequested(); Pump(); await Task.Delay(15, ct); } }
        Task Wait(Func<bool> condition, string label) => Until(() => Task.FromResult(condition()), label);
        async Task Until(Func<Task<bool>> condition, string label)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 20)
            { ct.ThrowIfCancellationRequested(); Pump(); if (await condition()) { Check(true, label); return; } await Task.Delay(20, ct); }
            throw new TimeoutException(label);
        }
        async Task<Room> Pair(Uri origin, string name, string policy, bool start)
        {
            var owner = await NetworkSession.ConnectAsync(origin, name + " owner", new SessionSettings(Bots: 0, Wins: 5, RoundSeconds: 300) { Hosting = policy }, cancellationToken: ct);
            clients.Add(owner);
            var guest = await NetworkSession.ConnectAsync(origin, name + " guest", new SessionSettings(Bots: 0), NetworkMode.JoinCode, owner.JoinCode, cancellationToken: ct);
            clients.Add(guest);
            await Wait(() => owner.Party?.CanStart == true && placements.ContainsKey(owner), name + " room admitted through actual player sockets");
            if (start) { owner.RequestStart(); await Wait(() => ticks.ContainsKey(owner) && ticks.ContainsKey(guest), name + " authoritative state reaches both players"); }
            return new(owner, guest);
        }
        async Task Leave(Room room)
        { await room.Guest.DisposeAsync(); await room.Owner.DisposeAsync(); clients.Remove(room.Guest); clients.Remove(room.Owner); }
        async Task<JsonElement> Fleet(Uri origin)
        {
            using var response = await http.GetAsync(new Uri(origin, "/realtime/v1/servers"), ct); response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return document.RootElement.Clone();
        }
    }

    private static int Used(JsonElement fleet, string server, string resource) => fleet.GetProperty("value").EnumerateArray()
        .Where(s => s.GetProperty("serverId").GetString() == server).SelectMany(s => s.GetProperty("hosted").EnumerateArray())
        .Sum(r => r.GetProperty("resourceClaims").TryGetProperty(resource, out var units) ? units.GetInt32() : 0);

    private sealed class Node
    {
        private readonly string _id, _dll, _output;
        private readonly Dictionary<string, string> _environment;
        private Process? _process;
        private Task<string>? _stdout, _stderr;
        private int _generation;
        private volatile bool _listening;
        public Uri Origin { get; }
        public Node(string id, int simulations, string dll, string plugins, string output, string advice, string redis, string nonce)
        {
            _id = id; _dll = dll; _output = output;
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            Origin = new Uri("http://127.0.0.1:" + port);
            _environment = new()
            {
                ["Kernel__NodeId"] = id, ["Kernel__Role"] = "worker", ["Kernel__Seed"] = "false", ["Kernel__PluginsDirectory"] = plugins,
                ["Realtime__Servers__0__Id"] = id, ["Realtime__Servers__0__Region"] = id, ["Realtime__Servers__0__MaxLobbies"] = "8",
                ["Realtime__Servers__0__PublicUrl"] = Origin.ToString().TrimEnd('/'),
                ["Realtime__Servers__0__AllowedGames__0"] = "blast-room", ["Realtime__Servers__0__AllowedGames__1"] = "blast-online",
                ["Realtime__Resources__simulation"] = simulations.ToString(), ["Realtime__Resources__peer-relay"] = "1",
                ["Realtime__Guests__SigningKey"] = "local-fleet-fixture-signing-key-" + nonce,
                ["Realtime__Redis__Configuration"] = redis, ["Realtime__Redis__KeyPrefix"] = "{blast-infra-" + nonce + "}:",
                ["Realtime__Placement"] = "blast-primary-fallback", ["Realtime__EnableWebSocketCompression"] = "true",
                ["BlastCluster__ServerOrder__0"] = "primary", ["BlastCluster__ServerOrder__1"] = "fallback", ["BlastCluster__AdmissionFile"] = advice,
                ["Auth__ApiKeys__0__Key"] = OperatorKey, ["Auth__ApiKeys__0__PrincipalId"] = "fixture", ["Auth__ApiKeys__0__Roles__0"] = "Owner",
            };
        }
        public async Task Start(CancellationToken ct)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(_dll); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(Origin.ToString().TrimEnd('/'));
            start.ArgumentList.Add("--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information");
            foreach (var pair in _environment) start.Environment[pair.Key] = pair.Value;
            _process = Process.Start(start) ?? throw new IOException("Node did not start");
            _listening = false;
            _stdout = Capture(_process.StandardOutput); _stderr = _process.StandardError.ReadToEndAsync(); _generation++;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 20)
            {
                if (_process.HasExited) throw new IOException("Owned node exited: " + await _stderr + await _stdout);
                try
                {
                    if (_listening)
                    {
                        using var response = await http.GetAsync(new Uri(Origin, "/realtime/v1/games"), ct);
                        if (!_process.HasExited && response.IsSuccessStatusCode && response.Headers.TryGetValues(OnlineProtocol.HeaderName, out var values) && OnlineProtocol.Accepts(values)) return;
                    }
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(100, ct);
            }
            throw new TimeoutException("Owned node readiness: " + _id);
        }
        private async Task<string> Capture(StreamReader reader)
        {
            var text = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line)
            {
                text.AppendLine(line);
                if (line.Contains("Now listening on: " + Origin.ToString().TrimEnd('/'), StringComparison.Ordinal)) _listening = true;
            }
            return text.ToString();
        }
        public async Task Stop()
        {
            if (_process is null) return;
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(_output, _id + "-" + _generation + ".log"), await _stdout! + await _stderr!);
            _process.Dispose(); _process = null;
        }
    }
    private static async Task<string> Command(string executable, string[] args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException(executable);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        string output = await stdout, error = await stderr;
        if (process.ExitCode != 0) throw new IOException(executable + ": " + error);
        return output;
    }
}
