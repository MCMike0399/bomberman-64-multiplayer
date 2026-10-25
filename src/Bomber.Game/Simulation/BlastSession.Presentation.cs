namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    // Sequence numbers survive round resets. Snapshots repeat four seconds of recent effects
    // so transient actions survive the snapshot boundary and brief delivery gaps. Old effects
    // must not consume bandwidth forever just because a battle has become quiet. Consumers baseline their
    // cursor on first state/reconnect, then consume seq > cursor, never infer actions from positions.
    private const int PresentationHistoryLimit = 128;
    private const int PresentationHistoryTicks = 4 * LocalMatch.TickRate;
    private bool _presentationDirty = true;
    private PresentationEvent[] _snapshotEvents = [];
    private readonly Queue<PresentationEvent> _presentationEvents = new();
    private long _presentationSeq;
    private readonly HashSet<(string PlayerId, string BombId)> _presentedKickContacts = [];

    private void ForgetSeparatedKickContacts(BlastPlayer player)
    {
        // A blocked rolling bomb can become resting each tick while the player keeps pushing.
        // Keep physics untouched, but report one kick for that continuous contact.
        var contactDistance = PlayerRadius + BlastBomb.Radius + .2f;
        _presentedKickContacts.RemoveWhere(contact => contact.PlayerId == player.Id &&
            !_bombs.Any(b => b.Id == contact.BombId && !b.Exploded &&
                Math.Abs(BombY(b) - player.Y) <= .6f &&
                Dist2(b.X, b.Z, player.X, player.Z) <= contactDistance * contactDistance));
    }

    private void Present(string kind, string? entityId = null, string? playerId = null,
        float x = 0, float y = 0, float z = 0, float magnitude = 0, float radius = 0,
        string? reason = null, string? causedBy = null)
    {
        TutorialEvent(kind,entityId,playerId,causedBy,reason);
        _presentationEvents.Enqueue(new PresentationEvent(++_presentationSeq, _tickCount, _roundNum,
            kind, entityId, playerId, x, y, z, magnitude, radius, reason, causedBy));
        if (_presentationEvents.Count > PresentationHistoryLimit) _presentationEvents.Dequeue();
        _presentationDirty = true;
    }

    private PresentationEvent[] SnapshotPresentationEvents()
    {
        while (_presentationEvents.TryPeek(out var oldest) && oldest.Tick < _tickCount - PresentationHistoryTicks)
        { _presentationEvents.Dequeue(); _presentationDirty = true; }
        if (_presentationDirty)
        { _snapshotEvents = _presentationEvents.ToArray(); _presentationDirty = false; }
        return _snapshotEvents;
    }

    private void PresentBomb(string kind, BlastBomb bomb, string? playerId, float magnitude = 0) =>
        Present(kind, bomb.Id, playerId, bomb.X, BombY(bomb), bomb.Z, magnitude, bomb.BlastRadius);

    // Public serialization properties use the shared realtime camelCase JSON policy.
    private sealed record PresentationEvent(long Seq, int Tick, int Round, string Kind,
        string? EntityId, string? PlayerId, float X, float Y, float Z, float Magnitude,
        float Radius, string? Reason, string? CausedBy);
}
