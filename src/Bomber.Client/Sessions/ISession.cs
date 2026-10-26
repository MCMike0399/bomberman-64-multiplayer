using System.Text.Json;
using Bomber.Game.Matches;

namespace Bomber.Client.Sessions;

/// <summary>Call Advance, SendInput and DrainMessages from the engine thread. No callbacks touch engine objects.</summary>
public interface ISession : IAsyncDisposable
{
    int LocalPlayerCount { get; }
    string Status { get; }
    string? JoinCode { get; }
    bool IsEnded { get; }
    void Advance(double deltaSeconds);
    void SendInput(int localPlayer, PlayerInput input);
    void ReleaseInputs();
    IReadOnlyList<JsonElement> DrainMessages();
}
