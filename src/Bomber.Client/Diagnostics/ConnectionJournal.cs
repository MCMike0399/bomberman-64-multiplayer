using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Bomber.Client.Diagnostics;

/// <summary>Bounded support history. Disk work is coalesced off transport and render threads.</summary>
internal sealed class ConnectionJournal : IAsyncDisposable
{
    internal const string FileName = "connection-events.json";
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Queue<Entry> _events = [], _spikes = [];
    private readonly Channel<Entry[]> _pending = Channel.CreateBounded<Entry[]>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _writer;
    private double? _previousRtt;
    private string? _lobbyId, _playerId;
    private static string? Identifier(string? value) => value is not null && Regex.IsMatch(value, @"^[A-Za-z0-9_-]{1,80}$") ? value : null;
    internal void Scope(string? lobbyId, string? playerId)
    { lock (_gate) { _lobbyId = Identifier(lobbyId); _playerId = Identifier(playerId); } }
    internal string Path => _path;
    internal bool WriteFailed { get; private set; }
    internal ConnectionJournal(string directory)
    {
        _path = System.IO.Path.Combine(System.IO.Path.GetFullPath(directory), FileName);
        try
        {
            if (File.Exists(_path) && new FileInfo(_path) is { LinkTarget: null, Length: <= 65536 })
                foreach (var entry in JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(_path)) ?? [])
                    if (entry is not null && entry.Text is not null && entry.Kind is "connection" or "rtt")
                        Append(entry with { Text = Redact(entry.Text), LobbyId = Identifier(entry.LobbyId), PlayerId = Identifier(entry.PlayerId) });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { WriteFailed = true; }
        _writer = Task.Run(Write);
    }
    internal static string Redact(string value)
    {
        value = value.Length <= 2048 ? value : value[..2048];
        string text = Regex.Replace(value, @"(?i)(?:https?|wss?)://[^\s]+", "<endpoint>");
        text = Regex.Replace(text, @"(?i)bearer\s+[^\s,;]+", "Bearer <redacted>");
        text = Regex.Replace(text, @"(?i)(token|ticket|secret|password|authorization)[""']?\s*[:=]\s*[""']?[^""'\s,;]+", "$1=<redacted>");
        var safe = new System.Text.StringBuilder(); int bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 180) break;
            safe.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return safe.ToString();
    }
    private void Append(Entry entry)
    {
        var queue = entry.Kind == "rtt" ? _spikes : _events;
        queue.Enqueue(entry); while (queue.Count > 64) queue.Dequeue();
    }
    private void Record(Entry entry)
    {
        lock (_gate)
        {
            Append(entry);
            _pending.Writer.TryWrite(_events.Concat(_spikes).OrderBy(x => x.Utc).ToArray());
        }
    }
    internal void Connection(int generation, string text) => Record(new(DateTimeOffset.UtcNow, "connection", generation, Redact(text), null, _lobbyId, _playerId));
    internal void Rtt(int generation, double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds is < 0 or > 60000) return;
        bool spike;
        lock (_gate) { spike = milliseconds > 250 || _previousRtt is { } previous && Math.Abs(milliseconds - previous) > 200; _previousRtt = milliseconds; }
        if (spike) Record(new(DateTimeOffset.UtcNow, "rtt", generation, "round-trip spike", Math.Round(milliseconds, 1), _lobbyId, _playerId));
    }
    private async Task Write()
    {
        await foreach (var entries in _pending.Reader.ReadAllAsync())
        {
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                string text = JsonSerializer.Serialize(entries);
                if (System.Text.Encoding.UTF8.GetByteCount(text) > 65536) throw new IOException("Connection journal exceeds its byte bound.");
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var stream = new FileStream(temporary, options))
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
                // Move the bounded regular file itself, never follow a previous
                // path's link or copy arbitrary pre-existing bytes into a backup.
                if (File.Exists(_path) && new FileInfo(_path) is { LinkTarget: null, Length: <= 65536 })
                    File.Move(_path, _path + ".previous", true);
                File.Move(temporary, _path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { WriteFailed = true; }
            finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { } }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { WriteFailed = true; }
    }
    internal sealed record Entry(DateTimeOffset Utc, string Kind, int Generation, string Text, double? RttMilliseconds, string? LobbyId = null, string? PlayerId = null);
}
