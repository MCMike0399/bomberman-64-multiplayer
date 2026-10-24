using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

// Real clients only: no authority injection, inventory edits, or synthetic snapshots.
internal static class CapacityProbe
{
    public static async Task Run(string[] args)
    {
        if (args.Length < 6) throw new ArgumentException("--capacity URL OUTPUT SECONDS ROOMS HUMANS BOTS [REGION]");
        var origin = new Uri(args[0]);
        string output = Path.GetFullPath(args[1]);
        int seconds = int.Parse(args[2]), count = int.Parse(args[3]), humans = int.Parse(args[4]), bots = int.Parse(args[5]);
        string? region = args.ElementAtOrDefault(6);
        bool directNode = args.Contains("--direct-node");
        if (seconds is < 20 or > 600 || count is < 1 or > 32 || humans < 1 || bots < 0 || humans + bots != 8)
            throw new ArgumentException("Capacity runs require 8 participants, 1–32 rooms, and 20–600 seconds.");
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        var rooms = Enumerable.Range(0, count).Select(i => new Room(origin, i, humans, bots, region, directNode)).ToArray();
        Exception? failure = null;
        DateTime started = DateTime.UtcNow;
        try
        {
            // Stagger setup to avoid an artificial simultaneous connection/JIT spike.
            foreach (var room in rooms) await room.Open(cancel.Token);
            Console.WriteLine($"READY {count} rooms, {count * humans} sockets, {count * bots} hard bots; region={region ?? "any"}");
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancel.Token);
        }
        catch (Exception e) { failure = e; }
        finally
        {
            // Stop every sender before any potentially slow HTTP cleanup.
            await Task.WhenAll(rooms.Select(room => room.StopClients()));
            foreach (var room in rooms) await room.Close();
            Console.CancelKeyPress -= handler;
        }
        var errors = rooms.SelectMany(r => r.Errors(seconds)).ToArray();
        bool passed = failure == null && errors.Length == 0;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, passed, started, completed = DateTime.UtcNow, seconds, count, humans, bots, region, directNode,
            inputHz = 30, failure = failure?.Message, errors,
            workload = "Legal maximum rule caps; production 30 Hz input cadence with movement, bomb/carry/pump/throw/remote/stop/aim; hard bots; normal deaths and rematches.",
            rooms = rooms.Select(r => r.Summary()).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} capacity probe: {output}");
        if (!passed)
        {
            Console.Error.WriteLine(failure?.Message ?? string.Join("; ", errors));
            Environment.ExitCode = 1;
        }
    }

    private sealed class Room(Uri origin, int index, int humans, int bots, string? region, bool directNode)
    {
        private readonly HttpClient _http = new() { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
        private readonly List<Client> _clients = [];
        private readonly List<string> _cleanupErrors = [];
        private string? _id;
        private string? _node;
        private string? _server;
        private string? _placedRegion;
        private string _stage = "";
        public async Task Open(CancellationToken cancel)
        {
            for (int i = 0; i < humans; i++)
            {
                var guest = await Api("guests", new { displayName = $"Capacity {index}-{i}" }, null, cancel);
                _clients.Add(new Client(guest.GetProperty("token").GetString()!, i));
            }
            _stage = new[] { "sunken-temple", "rock-garden", "up-and-down" }[index % 3];
            var settings = new Dictionary<string, string>
            {
                ["party"] = "on", ["stage"] = _stage, ["bots"] = bots.ToString(), ["wins"] = "5",
                ["round-seconds"] = "120", ["bot-difficulty"] = "hard", ["ghosts"] = "on", ["sudden-death"] = "on",
                ["drop-percent"] = "100", ["block-percent"] = "100", ["item-seconds"] = "60",
                ["max-fire"] = "5", ["max-bombs"] = "8", ["effect-seconds"] = "30"
            };
            var created = await Api("lobbies", new { game = "blast", visibility = "private", region, settings }, _clients[0].Token, cancel);
            var lobby = created.GetProperty("lobby");
            _id = lobby.GetProperty("lobbyId").GetString();
            _node = lobby.TryGetProperty("nodeId", out var node) ? node.GetString() : null;
            _server = lobby.TryGetProperty("serverId", out var server) ? server.GetString() : null;
            _placedRegion = lobby.TryGetProperty("region", out var placed) ? placed.GetString() : null;
            if (region != null && !string.Equals(region, _placedRegion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Requested region {region} had no capacity; refusing to load {_placedRegion} instead.");
            string code = lobby.GetProperty("joinCode").GetString()!;
            await _clients[0].Connect(origin, created.GetProperty("reservation"), directNode, cancel);
            for (int i = 1; i < humans; i++)
                await _clients[i].Connect(origin, await Api("lobbies/join", new { code }, _clients[i].Token, cancel), directNode, cancel);
            foreach (var client in _clients) client.Start();
            _clients[0].Leader = true;
        }

        private async Task<JsonElement> Api(string path, object body, string? token = null, CancellationToken cancel = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/realtime/v1/" + path) { Content = JsonContent.Create(body) };
            if (token != null) request.Headers.Add("x-player-token", token);
            using var response = await _http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"{path.Split('/')[0]}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancel)}");
            return response.StatusCode == System.Net.HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>(cancel);
        }

        public Task StopClients() => Task.WhenAll(_clients.Select(client => client.Stop()));

        public async Task Close()
        {
            if (_id != null)
                foreach (var client in _clients)
                    try { await Api($"lobbies/{_id}/leave", new { }, client.Token); }
                    catch (Exception e) { _cleanupErrors.Add(e.Message); }
            _http.Dispose();
        }

        public IEnumerable<string> Errors(int seconds)
        {
            if (_clients.Count != humans) yield return $"room {index}: incomplete setup";
            if (region != null && _placedRegion != region) yield return $"room {index}: placed in {_placedRegion}, requested {region}";
            foreach (var client in _clients)
            {
                if (client.Error != null) yield return $"room {index}, seat {client.Seat}: {client.Error}";
                if (client.Samples.Count < seconds * 5 || client.LastHumans != humans || client.LastBots != bots)
                    yield return $"room {index}, seat {client.Seat}: missing sustained eight-player snapshots";
                if (client.UniqueEvents.GetValueOrDefault("bomb-drop") == 0 && client.UniqueEvents.GetValueOrDefault("blast") == 0)
                    yield return $"room {index}, seat {client.Seat}: no accepted bomb activity";
                foreach (string error in client.TimingErrors()) yield return $"room {index}, seat {client.Seat}: {error}";
            }
            foreach (string error in _cleanupErrors) yield return $"room {index} cleanup: {error}";
        }

        public object Summary() => new { index, id = _id, node = _node, server = _server, region = _placedRegion, stage = _stage, clients = _clients.Select(c => c.Summary()).ToArray(), cleanupErrors = _cleanupErrors };
    }

    private sealed record Sample(double At, long ServerAt, long Tick, int Match, int Bytes, int Bombs, int Hazards, int Items, int Events, int Alive, int Phase);

    private sealed class Client(string token, int seat)
    {
        public string Token => token;
        public int Seat => seat;
        public readonly List<Sample> Samples = [];
        public readonly Dictionary<string, int> UniqueEvents = [];
        public int LastHumans, LastBots;
        public string? Error;
        public volatile bool Leader;
        private volatile bool _canStart;
        private int _match;
        private long _lastEvent;
        private readonly List<double> _pings = [];
        private readonly CancellationTokenSource _stop = new();
        private readonly ClientWebSocket _socket = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private Task? _reader, _writer;
        private long _sent;
        private double _stoppedAt;
        private string? _connectPath;
        private bool _compressed;

        public async Task Connect(Uri origin, JsonElement reservation, bool directNode, CancellationToken cancel)
        {
            var url = new UriBuilder(new Uri(origin, reservation.GetProperty("connectUrl").GetString()!));
            if (directNode) url = new UriBuilder(origin) { Path = "/realtime/v1/connect", Query = url.Query };
            url.Scheme = url.Scheme switch { "https" or "wss" => "wss", "http" or "ws" => "ws", _ => throw new ArgumentException("Invalid socket scheme") };
            if (origin.Scheme == "https" && url.Scheme != "wss") throw new InvalidDataException("Insecure socket reservation");
            _connectPath = url.Path; // Never retain the reservation token/query.
            _socket.Options.CollectHttpResponseDetails = true;
            _socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions { ClientContextTakeover = false, ServerContextTakeover = false };
            await _socket.ConnectAsync(url.Uri, cancel);
            _compressed = _socket.HttpResponseHeaders!.TryGetValue("Sec-WebSocket-Extensions", out var extensions) && string.Join(",", extensions).Contains("permessage-deflate", StringComparison.Ordinal);
            _reader = Read();
        }
        public void Start() => _writer = Write();
        private Task Send(object value) => _socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(value)), WebSocketMessageType.Text, true, _stop.Token);

        private async Task Write()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 30));
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    if (Leader && _canStart) { _canStart = false; await Send(new { t = "party.start", d = new { } }); }
                    long seq = ++_sent;
                    int direction = (int)(_clock.Elapsed.TotalSeconds / 1.25 + seat) % 4;
                    int x = direction == 0 ? 1 : direction == 2 ? -1 : 0;
                    int z = direction == 1 ? 1 : direction == 3 ? -1 : 0;
                    // Match NetworkSession's 30 Hz sender, with a new action edge every tick.
                    await Send(new { t = "input", s = seq, d = new { x, z, a = seq % 2 == 0, b = seq % 180 is >= 120 and < 150, r = seq % 15 == 0, stop = seq % 45 == 0, aim = seq % 180 is >= 125 and < 145 } });
                    if (seq % 15 == 0) await Send(new { t = "sys.ping", d = new { sent = Stopwatch.GetTimestamp() } });
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception e) { if (!_stop.IsCancellationRequested) Error = e.Message; }
        }

        private async Task Read()
        {
            var buffer = new byte[65536];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var bytes = new MemoryStream();
                    WebSocketReceiveResult response;
                    do
                    {
                        response = await _socket.ReceiveAsync(buffer, _stop.Token);
                        if (response.MessageType == WebSocketMessageType.Close) throw new IOException("Unexpected socket close");
                        bytes.Write(buffer, 0, response.Count);
                        if (bytes.Length > 1024 * 1024) throw new IOException("Oversized server frame");
                    } while (!response.EndOfMessage);
                    using var doc = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length));
                    var frame = doc.RootElement; var data = frame.GetProperty("d");
                    switch (frame.GetProperty("t").GetString())
                    {
                        case "party.status":
                            _canStart = data.GetProperty("phase").GetString() == "lobby" && data.GetProperty("canStart").GetBoolean();
                            int match = data.GetProperty("match").GetInt32();
                            if (match != _match) { _lastEvent = 0; _match = match; }
                            break;
                        case "sys.pong": _pings.Add(Stopwatch.GetElapsedTime(data.GetProperty("echo").GetProperty("sent").GetInt64()).TotalMilliseconds); break;
                        case "sys.error": throw new IOException("Server error: " + data.GetProperty("code").GetString());
                        case "state":
                            LastHumans = LastBots = 0; int alive = 0;
                            foreach (var player in data.GetProperty("players").EnumerateArray())
                            {
                                if (player.GetProperty("bot").GetBoolean()) LastBots++; else LastHumans++;
                                if (player.GetProperty("alive").GetBoolean()) alive++;
                            }
                            var events = data.GetProperty("events");
                            foreach (var item in events.EnumerateArray())
                            {
                                long seq = item.GetProperty("seq").GetInt64();
                                if (seq <= _lastEvent) continue;
                                _lastEvent = seq; string kind = item.GetProperty("kind").GetString()!;
                                UniqueEvents[kind] = UniqueEvents.GetValueOrDefault(kind) + 1;
                            }
                            Samples.Add(new(_clock.Elapsed.TotalMilliseconds, data.GetProperty("serverTime").GetInt64(), frame.GetProperty("k").GetInt64(), _match,
                                (int)bytes.Length, data.GetProperty("bombs").GetArrayLength(), data.GetProperty("hazards").GetArrayLength(),
                                data.GetProperty("items").GetArrayLength(), events.GetArrayLength(), alive, data.GetProperty("phase").GetInt32()));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception e) { if (!_stop.IsCancellationRequested) Error = e.Message; }
        }

        public async Task Stop()
        {
            _stoppedAt = _clock.Elapsed.TotalSeconds;
            _stop.Cancel(); _socket.Abort();
            if (_reader != null) await _reader;
            if (_writer != null) await _writer;
            _socket.Dispose();
        }
        public object Summary()
        {
            var steady = Samples.Where(s => s.At >= 10000).ToArray();
            var pairs = steady.Zip(steady.Skip(1)).Where(p => p.First.Match == p.Second.Match).ToArray();
            return new
            {
                seat, connectPath = _connectPath, compressed = _compressed, error = Error, sentInputs = _sent, snapshots = Samples.Count,
                elapsedSeconds = _stoppedAt, decodedBytesPerSecond = Samples.Sum(s => (long)s.Bytes) / Math.Max(1, _stoppedAt),
                arrivalGapMs = Stats(pairs.Select(p => p.Second.At - p.First.At)),
                serverGenerationGapMs = Stats(pairs.Select(p => (double)(p.Second.ServerAt - p.First.ServerAt))), pingMs = Stats(_pings),
                snapshotBytes = Stats(steady.Select(s => (double)s.Bytes)), bombs = Stats(steady.Select(s => (double)s.Bombs)),
                hazards = Stats(steady.Select(s => (double)s.Hazards)), items = Stats(steady.Select(s => (double)s.Items)),
                alive = Stats(steady.Select(s => (double)s.Alive)), uniqueEvents = UniqueEvents,
                timeline = steady.Where((_, i) => i % 15 == 0).ToArray()
            };
        }
        public IEnumerable<string> TimingErrors()
        {
            var steady = Samples.Where(s => s.At >= 10000).ToArray();
            var pairs = steady.Zip(steady.Skip(1)).Where(p => p.First.Match == p.Second.Match).ToArray();
            double Q(IEnumerable<double> source, double q)
            {
                var sorted = source.Order().ToArray();
                return sorted.Length == 0 ? double.PositiveInfinity : sorted[(int)Math.Ceiling((sorted.Length - 1) * q)];
            }
            double server = Q(pairs.Select(p => (double)(p.Second.ServerAt - p.First.ServerAt)), .99);
            double delivery = Q(pairs.Select(p => p.Second.At - p.First.At), .99);
            if (server > 150) yield return $"p99 generation gap {server:F1} ms exceeds 150 ms";
            if (delivery > 300) yield return $"p99 delivery gap {delivery:F1} ms exceeds 300 ms";
        }
    }
    private static object Stats(IEnumerable<double> source)
    {
        var data = source.Order().ToArray();
        double Q(double q) => data.Length == 0 ? 0 : data[(int)Math.Ceiling((data.Length - 1) * q)];
        return new { count = data.Length, mean = data.Length == 0 ? 0 : data.Average(), p50 = Q(.5), p95 = Q(.95), p99 = Q(.99), max = Q(1) };
    }
}
