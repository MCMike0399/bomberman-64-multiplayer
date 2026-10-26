using Bomber.Game.Matches;
using static GameFixtures;

/// <summary>Participant validation, seats, stage payloads, determinism, input edges and round flow.</summary>
internal static class MatchRulesChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("participant bounds and stage validation", () =>
        {
            foreach (var options in new[] {
                new MatchOptions { HumanPlayers = 0 }, new MatchOptions { HumanPlayers = 8, BotPlayers = 1 },
                new MatchOptions { HumanPlayers = 1, BotPlayers = 0 }, new MatchOptions { Stage = "missing" },
                new MatchOptions { Wins = 0 }, new MatchOptions { RoundSeconds = 59 } })
            {
                var threw = false;
                try { _ = new LocalMatch(options); } catch (ArgumentException) { threw = true; }
                Assert(threw, "Invalid options must be rejected.");
            }
        });
        run("eight humans have unique seats and spawns", () =>
        {
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 8, BotPlayers = 0 });
            var players = State(match).GetProperty("players").EnumerateArray().ToArray();
            Assert(players.Length == 8, "Expected eight humans.");
            Assert(players.All(p => !p.GetProperty("bot").GetBoolean()), "Human seats became bots.");
            Assert(players.Select(p => p.GetProperty("slot").GetInt32()).Distinct().Count() == 8, "Duplicate seat.");
            Assert(players.Select(p => (p.GetProperty("x").GetSingle(), p.GetProperty("z").GetSingle())).Distinct().Count() == 8, "Duplicate spawn.");
        });
        run("stage payload preserves raised terrain and complete soft grid", () =>
        {
            var match = new LocalMatch(new MatchOptions { Stage = "up-and-down" });
            var stage = Data(match.StageJson!);
            var width = stage.GetProperty("width").GetInt32();
            var height = stage.GetProperty("height").GetInt32();
            var cells = stage.GetProperty("cells").EnumerateArray().ToArray();
            Assert(cells.Length == width * height, "Incomplete stage grid.");
            Assert(cells.Any(c => c.GetProperty("t").GetInt32() > 0), "No raised terrain.");
            Assert(cells.Any(c => c.GetProperty("k").GetInt32() == 4), "No ramp.");
            Assert(Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!).Length > 0, "Empty soft grid.");
        });
        run("seeded bots and human inputs replay deterministically", () =>
        {
            var options = new MatchOptions { BotPlayers = 7, Seed = 9876 };
            var a = new LocalMatch(options); var b = new LocalMatch(options);
            for (var i = 0; i < 1800; i++)
            {
                var input = new PlayerInput(i % 90 < 45 ? .3f : -.3f, 0, i % 45 == 0);
                a.SetInput(0, input); b.SetInput(0, input); a.Tick(); b.Tick();
                Assert(a.StateJson == b.StateJson, $"Replay diverged at tick {i}.");
                Assert(a.IsEnded == b.IsEnded, "End state diverged.");
            }
        });
        run("bomb press is edge triggered and explodes after fuse", () =>
        {
            var match = TwoHumans();
            Fight(match);
            match.SetInput(0, new PlayerInput(0, 0, true)); Step(match, 2);
            Assert(State(match).GetProperty("bombs").GetArrayLength() == 1, "Bomb was not placed.");
            match.SetInput(0,default);Step(match,20);
            Assert(State(match).GetProperty("bombs").GetArrayLength() == 1, "One A tap placed extra bombs.");
            var sawHazard = false;
            match.Message += json => { if (Data(json).TryGetProperty("hazards", out var hazards) && hazards.GetArrayLength() > 0) sawHazard = true; };
            Step(match, 70);
            Assert(sawHazard, "Bomb never generated a spherical explosion.");
            Assert(State(match).GetProperty("bombs").GetArrayLength() == 0, "Bomb survived its fuse.");
            Assert(State(match).GetProperty("players")[0].GetProperty("ghost").GetBoolean(), "Explosion did not turn its victim into a ghost.");
        });
        run("nonfinite input is safe and movement acknowledges input", () =>
        {
            var match = TwoHumans(); Fight(match);
            var before = State(match).GetProperty("players")[0].GetProperty("x").GetSingle();
            match.SetInput(0, new PlayerInput(float.NaN, float.PositiveInfinity)); Step(match, 2);
            var player = State(match).GetProperty("players")[0];
            Assert(player.GetProperty("x").GetSingle() == before, "Nonfinite input moved player.");
            match.SetInput(0, new PlayerInput(1, 0)); Step(match, 4);
            player = State(match).GetProperty("players")[0];
            Assert(player.GetProperty("x").GetSingle() > before, "Valid movement did not move player.");
            Assert(player.GetProperty("ack").GetInt64() > 0, "Input not acknowledged.");
        });
        run("round timeout draws and starts a fresh round", () =>
        {
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 2, BotPlayers = 0, RoundSeconds = 60, SuddenDeath = false });
            Step(match, 2000);
            var state = State(match);
            Assert(state.GetProperty("round").GetInt32() == 2, "Draw did not advance round.");
            Assert(state.GetProperty("players").EnumerateArray().All(p => p.GetProperty("wins").GetInt32() == 0), "Draw awarded a win.");
        });
    }
}
