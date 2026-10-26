
namespace Bomber.Client.Sessions;

public sealed record LobbyParticipant(string Id, string Name, int Slot, bool Connected);
