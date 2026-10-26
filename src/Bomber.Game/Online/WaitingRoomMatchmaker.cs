using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Online;

public sealed class WaitingRoomMatchmaker : IMatchmakingStrategy
{
    public IReadOnlyList<MatchDecision> Decide(MatchmakingPool pool)
    {
        var tickets = new Queue<MatchTicket>(pool.Tickets.OrderBy(t => t.EnqueuedAt).ThenBy(t => t.TicketId, StringComparer.Ordinal));
        var decisions = new List<MatchDecision>();
        foreach (var room in pool.Backfill.OrderBy(r => r.LobbyId, StringComparer.Ordinal))
        {
            var seats = new List<string>();
            // This game's public queue is global; explicit region tickets only backfill their region.
            foreach (var ticket in tickets.ToArray().Where(t => t.Region is null || t.Region == room.Region).Take(room.OpenSeats))
                seats.Add(ticket.TicketId);
            if (seats.Count == 0) continue;
            tickets = new Queue<MatchTicket>(tickets.Where(t => !seats.Contains(t.TicketId)));
            decisions.Add(new BackfillDecision(room.LobbyId, seats));
        }
        foreach (var region in tickets.GroupBy(t => t.Region))
            foreach (var group in region.Chunk(pool.Rules.MaxPlayers))
                decisions.Add(new NewLobbyDecision(region.Key, group.Select(t => t.TicketId).ToArray()));
        return decisions;
    }
}
