using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

/// <summary>Terraces, ramps, temple topology and altitude-aware blast volumes.</summary>
internal static class TerrainChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("vertical terrace walls cannot be walked through", () =>
        {
            var arena = StepArena((3, 2));
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }, arena);
            Fight(match); match.SetInput(0, new PlayerInput(1, 0)); Step(match, 15);
            var player = State(match).GetProperty("players")[0];
            Assert(player.GetProperty("x").GetSingle() <= 3.61f, "Walked through a terrace wall without stairs.");
            Assert(player.GetProperty("y").GetSingle() == 0, "Climbed a vertical cliff.");
        });
        run("actual ramp ascent and descent publish continuous authority height", () =>
        {
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }, StepArena((2, 3)));
            Fight(match); match.SetInput(0, new PlayerInput(1, 0));
            var sawSlope = false;
            for (var i = 0; i < 16; i++)
            {
                match.Tick(); var y = State(match).GetProperty("players")[0].GetProperty("y").GetSingle();
                sawSlope |= y > .05f && y < .95f;
            }
            Assert(sawSlope, "Ramp height snapped between integer tiers.");
            Assert(State(match).GetProperty("players")[0].GetProperty("y").GetSingle() == 1, "Ramp ascent failed.");
            match.SetInput(0, new PlayerInput(-1, 0)); Step(match, 16);
            var player = State(match).GetProperty("players")[0];
            Assert(player.GetProperty("x").GetSingle() < 3 && player.GetProperty("y").GetSingle() == 0, "Ramp descent failed.");
        });
        run("temple topology connects all eight spawns with sparse clear stairs", () =>
        {
            var stage = BlastStages.Get("sunken-temple");
            Assert(stage.Width == 25 && stage.Height == 27, "Temple dimensions changed.");
            Assert(stage.Spawns.Distinct().Count() == 8, "Temple must support eight distinct spawns.");
            var seen = new HashSet<(int X, int Z)> { stage.Spawns[0] }; var queue = new Queue<(int X, int Z)>(); queue.Enqueue(stage.Spawns[0]);
            while (queue.TryDequeue(out var cell)) foreach (var (dx, dz) in new[] { (1,0),(-1,0),(0,1),(0,-1) })
            {
                var next = (cell.X + dx, cell.Z + dz);
                if (stage.CanCross(cell.X, cell.Z, next.Item1, next.Item2) && seen.Add(next)) queue.Enqueue(next);
            }
            Assert(stage.Spawns.All(seen.Contains), "A spawn is cut off from the shared arena.");
            Assert(seen.Contains((12,2)) && seen.Contains((12,18)), "Crown or sunken court disconnected.");
            for (var seed = 0; seed < 30; seed++)
            {
                var match = new LocalMatch(new MatchOptions { Seed = seed });
                var bits = Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                Assert(bits.Sum(b => System.Numerics.BitOperations.PopCount((uint)b)) <= 90, "Temple exceeded expanded cover budget.");
                for (var z = 3; z <= 13; z++) for (var x = 11; x <= 13; x++)
                    Assert((bits[(z*25+x)/8] & 1 << ((z*25+x)%8)) == 0, "Central stair route blocked by cover.");
            }
        });
        run("temple actual authority inputs traverse summit and sunken court round trip", () =>
        {
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 1, BotPlayers = 0, Practice = true }); Fight(match);
            Walk(match, 12.5f, 2.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 5, "Did not reach fifth-tier crown through four stairs.");
            Walk(match, 12.5f, 12.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 1, "Did not descend three stairs.");
            Walk(match, 8.5f, 12.5f); Walk(match, 8.5f, 17.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 0, "Did not descend into courtyard.");
            Walk(match, 8.5f, 12.5f); Walk(match, 12.5f, 12.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 1, "Did not ascend from courtyard.");
            Step(match, 10000);
            Assert(!match.IsEnded && State(match).GetProperty("round").GetInt32() == 1, "Practice ended or reset while exploring.");
        });
        run("blast volumes respect altitude and stone even with piercing power", () =>
        {
            var stage = FlatArena([(1,1)]); var cover = new SoftBlockGrid(stage.Width, stage.Height);
            Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 1.3f, 3.5f, 2, 3.5f), "Blast ignored vertical separation.");
            Assert(BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 3, 3.5f, 2, 3.5f), "Large spherical blast did not include high target.");
            stage.Cells[3,3] = CellDef.Pillar;
            Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f), "Stone failed to occlude blast.");
            Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f, true), "Piercing blast passed hard stone.");
            stage.Cells[3,3] = CellDef.Floor2;
            Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f, true), "Blast passed raised terrain.");
            stage.Cells[3,3] = CellDef.Floor0; cover.Set(3,3,true);
            Assert(!BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f), "Soft cover failed to shield target.");
            Assert(BlastGeometry.Contains(stage, cover, 2.5f, 0, 3.5f, 4, 4.5f, 0, 3.5f,true), "Piercing blast did not pass soft cover.");
            Assert(BlastGeometry.Contains(stage, cover, 2.5f, 3, 3.5f, 4, 4.5f, 3, 3.5f), "Low cover blocked a blast well above it.");
        });
        run("detonation does not hit a nearby player two levels overhead", () =>
        {
            var stage = FlatArena([(2,3),(3,3)]); stage.Cells[3,3] = CellDef.Floor2;
            var match = new LocalMatch(new MatchOptions { HumanPlayers = 2, BotPlayers = 0, Practice = true }, stage); Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true)); Step(match,2);match.SetInput(0,default);Step(match,88);
            var high = Player(match,1);
            Assert(high.GetProperty("alive").GetBoolean() && !high.GetProperty("ghost").GetBoolean(), "Ground-level bomb killed elevated player outside its sphere.");
        });
    }
}
