using System.Text.Json;

namespace Bomber.Client.Sessions;

/// <summary>Full state snapshots may supersede older snapshots, but stage/control frames stay ordered.</summary>
internal sealed class MessageBuffer
{
    private readonly object _gate = new();
    private readonly LinkedList<JsonElement> _messages = new();
    private const int Capacity = 128;
    private volatile bool _overflowed;
    public bool Overflowed => _overflowed;
    public void Add(JsonElement message)
    {
        lock (_gate)
        {
            string? stateKey = StateKey(message);
            if (stateKey is not null)
                for (var node = _messages.Last; node != null; node = node.Previous)
                    if (StateKey(node.Value) == stateKey) { _messages.Remove(node); break; }
            if (_messages.Count == Capacity)
            {
                var expendable = _messages.First;
                while (expendable is not null && StateKey(expendable.Value) is null) expendable = expendable.Next;
                if (expendable is not null) _messages.Remove(expendable);
                else { if (stateKey is null) _overflowed = true; return; }
            }
            _messages.AddLast(message.Clone());
        }
    }
    private static string? StateKey(JsonElement message)
    {
        string? type = message.GetProperty("t").GetString();
        if (type == "state") return type;
        if (type == "peer.state" && message.TryGetProperty("d", out var data) && data.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String)
            return "peer:" + from.GetString();
        return null;
    }
    public IReadOnlyList<JsonElement> Drain()
    {
        lock (_gate) { var result = _messages.ToArray(); _messages.Clear(); return result; }
    }
    public void Clear() { lock (_gate) { _messages.Clear(); _overflowed = false; } }
}
