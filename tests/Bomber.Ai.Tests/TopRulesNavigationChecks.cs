using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class TopRulesNavigationChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        var stage = BlastStages.Get(BlastStages.TopRules);
        var graph = NavigationGraph.For(stage);
        int high = graph.Cell(5.5f, 4.5f, 2), low = graph.Cell(5.5f, 4.5f, 0);
        Check(high != low && graph.Height[high] == 2 && graph.Height[low] == 0, "CPU graph distinguishes bridge and underpass nodes");
        var seen = new HashSet<int> { graph.Cell(5.5f, 2.5f, 2) };
        var queue = new Queue<int>(seen);
        while (queue.TryDequeue(out int node))
            for (int d = 0; d < 4; d++)
            {
                int next = graph.Edges[node * 4 + d];
                if (next >= 0 && seen.Add(next)) queue.Enqueue(next);
            }
        Check(seen.Contains(high) && seen.Contains(low) && seen.Contains(graph.Cell(16.5f, 6.5f, 0)), "CPU routes connect both stories through the real stairs and drops");
        Check(graph.Edges[low * 4 + 1] == graph.Cell(4.5f, 4.5f, 0) && graph.Edges[low * 4 + 2] == graph.Cell(6.5f, 4.5f, 0), "CPU lower route crosses bridge projection on the red floor");
        var soft = new SoftBlockGrid(stage.Width, stage.Height);
        var danger = new TimedDangerField(graph, stage);
        danger.Build(soft, [new BlastBomb { Id = "lower", X = 5.5f, Z = 4.5f, Tier = 0, FuseTimer = 1 }], []);
        Check(!danger.Safe(low, 1, 1.3f) && danger.Safe(high, 0, 2), "CPU danger keeps a lower fuse below the solid deck");
        Check(danger.BombOwners[low] is not null && danger.BombOwners[high] is null, "CPU bomb occupancy belongs to the correct story");
        danger.Build(soft, [new BlastBomb { Id="airborne",X=2.5f,Z=6.5f,Tier=0,State=BombState.Flying,FlyY=3,FlyVX=6,FuseTimer=.5f }], []);
        Check(!danger.Safe(graph.Cell(5.5f,6.5f,2),.5f,.8f), "CPU projects a lower-origin airborne throw onto the upper deck using its actual height");
        foreach (string difficulty in new[] { "easy", "normal", "hard" })
        {
            var fixture = new Fixture(stage, bots: 2, difficulty: difficulty);
            fixture.Step(62);
            var bot = fixture.Session.AiTestPlayers.First();
            var other = fixture.Session.AiTestPlayers.Last();
            // Isolated route witness: no bombs/cover/body blocks. Only the bot's
            // ordinary steering may move it (pickup within Easy's six-cell search) from the lower court to the upper target.
            bot.X = 3.5f; bot.Z = 6.5f; bot.SurfaceY = 0; bot.Tier = 0;
            bot.BombCapacity = other.BombCapacity = 0; other.BotThinkTimer = 10000;
            other.X = 13.5f; other.Z = 1.5f; other.SurfaceY = 2; other.Tier = 2;
            foreach (int x in Enumerable.Range(0, stage.Width)) foreach (int z in Enumerable.Range(0, stage.Height))
                fixture.Session.AiTestSetSoft(x, z, false);
            fixture.Session.AiTestAddItem(2, 1, "heart");
            bool climbed = false, moved = false;
            for (int tick = 0; tick < 600; tick++)
            { fixture.Step(); climbed |= bot.Y >= 1.99f; moved |= Math.Abs(bot.X - 3.5f) + Math.Abs(bot.Z - 6.5f) > 2; }
            Check(climbed && moved && bot.Alive, difficulty + $" CPU leaves lower court and climbs actual castle stairs without teleporting; end=({bot.X:F2},{bot.Y:F2},{bot.Z:F2}) input={bot.Input}");
        }
        return checks;
    }
}
