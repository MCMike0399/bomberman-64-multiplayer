using System.Text.Json;
using Bomber.Game.Matches;

namespace Bomber.Client.Sessions;

public sealed class LocalSession : ISession
{
    private readonly LocalMatch _match;
    private readonly MessageBuffer _messages = new();
    private double _accumulator;
    private readonly PlayerInput[] _inputs;
    private readonly Queue<(bool A, bool B, bool R, bool Aim, bool Stop)>[] _buttons;
    private bool _disposed;
    public int LocalPlayerCount { get; }
    public string Status => _disposed ? "Ended" : _match.IsEnded ? "Ended: Match complete" : "Local battle";
    public bool IsEnded => _disposed || _match.IsEnded;
    public string? JoinCode => null;
    public LocalSession(SessionSettings settings, int humanCount = 1, int seed = 64, bool practice = false, IReadOnlyList<int>? playerAppearances = null, string? tutorialLesson = null)
    {
        (practice && settings.Stage == "circuit-garden" ? settings with {Stage="sunken-temple"} : settings).ToWireSettings();
        if (humanCount is < 1 or > 8 || humanCount + settings.Bots > 8 || humanCount + settings.Bots < (practice ? 1 : 2))
            throw new ArgumentOutOfRangeException(nameof(humanCount), "Choose 1–8 humans and 2–8 total players.");
        LocalPlayerCount = humanCount;
        _inputs = new PlayerInput[humanCount];
        _buttons = Enumerable.Range(0, humanCount).Select(_ => new Queue<(bool A, bool B, bool R, bool Aim, bool Stop)>()).ToArray();
        _match = new LocalMatch(new MatchOptions { HumanPlayers = humanCount, BotPlayers = settings.Bots, Advanced=settings.Advanced,
            Stage = settings.Stage, BotDifficulty = settings.BotDifficulty, Wins = settings.Wins, RoundSeconds = settings.RoundSeconds,
            SuddenDeath = settings.SuddenDeath, Ghosts = settings.Ghosts, Seed = seed, Practice = practice, TutorialLesson=tutorialLesson, PlayerAppearances = playerAppearances ?? Array.Empty<int>() });
        _match.Message += OnMessage;
        if (_match.StageJson is { } stage) OnMessage(stage);
        if (_match.StateJson is { } state) OnMessage(state);
    }
    private void OnMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        _messages.Add(document.RootElement);
    }
    public void Advance(double deltaSeconds)
    {
        if (_disposed || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        // Bound catch-up after a paused/dragged window instead of blocking the engine thread.
        _accumulator += Math.Min(deltaSeconds, 5d / 30);
        while (_accumulator >= 1d / 30)
        {
            for (var i = 0; i < LocalPlayerCount; i++)
            {
                var input = _inputs[i];
                if (_buttons[i].TryDequeue(out var buttons)) input = input with { A = buttons.A, B = buttons.B, R = buttons.R, Aim = buttons.Aim, Stop = buttons.Stop };
                _match.SetInput(i, input);
            }
            _match.Tick();
            _accumulator -= 1d / 30;
        }
    }
    public void SendInput(int localPlayer, PlayerInput input)
    {
        if (localPlayer < 0 || localPlayer >= LocalPlayerCount) throw new ArgumentOutOfRangeException(nameof(localPlayer));
        if (_disposed) return;
        var previous = _inputs[localPlayer];
        if ((previous.A, previous.B, previous.R, previous.Aim, previous.Stop) != (input.A, input.B, input.R, input.Aim, input.Stop))
        {
            if (_buttons[localPlayer].Count == 16) _buttons[localPlayer].Dequeue();
            _buttons[localPlayer].Enqueue((input.A, input.B, input.R, input.Aim, input.Stop));
        }
        _inputs[localPlayer] = input;
    }
    public void ReleaseInputs() { Array.Clear(_inputs); foreach(var queue in _buttons) queue.Clear(); }
    public IReadOnlyList<JsonElement> DrainMessages() => _messages.Drain();
    public ValueTask DisposeAsync()
    {
        if (!_disposed) { _disposed = true; _match.Message -= OnMessage; }
        return ValueTask.CompletedTask;
    }
}
