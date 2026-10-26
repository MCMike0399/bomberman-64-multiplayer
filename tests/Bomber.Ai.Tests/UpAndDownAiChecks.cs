using System.Reflection;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class UpAndDownAiChecks
{
    public static int Run()
    {
        var stage=BlastStages.Get(BlastStages.UpAndDown);var graph=NavigationGraph.For(stage);
        var soft=new SoftBlockGrid(stage.Width,stage.Height);var danger=new TimedDangerField(graph,stage);
        var bomb=new BlastBomb {Id="underpass",X=18.5f,Z=21.5f,Tier=2,LandingY=2,FireLevel=3,FuseTimer=.5f};
        danger.Build(soft,[bomb],[]);
        int upper=graph.Cell(bomb.X,bomb.Z,4),lower=graph.Cell(bomb.X,bomb.Z,2);
        Check(danger.BombOwners[lower]!=null&&danger.BombOwners[upper]==null,"underpass bomb occupancy belongs to the lower CPU node");
        Check(!danger.Safe(lower,.6f,.8f)&&danger.Safe(upper,.6f,.8f),"underpass danger cannot leak through the upper slab");
        // Explicit AI fixture: real stage geometry, lower starts and fixed visible
        // cover. This isolates planting/escape below, rather than requiring a
        // winning upper-ring CPU to abandon its advantageous position.
        var farming=new StageLayout {Id=stage.Id,Width=stage.Width,Height=stage.Height,Cells=stage.Cells,
            Spawns=[(11,16),(5,17),(18,17)],FixedSoftBlocks=new HashSet<(int,int)>{(10,16),(6,17),(19,17)}};
        var fixture=new Fixture(farming,bots:3);int initial=3,minimum=3;bool lowerBomb=false;
        var cover=(SoftBlockGrid)typeof(BlastSession).GetField("_softBlocks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(fixture.Session)!;
        for(int tick=0;tick<1000;tick++)
        {
            fixture.Step();
            lowerBomb|=fixture.Session.AiTestBombs.Any(b=>b.Tier<4);
            minimum=Math.Min(minimum,farming.FixedSoftBlocks!.Count(c=>cover.IsAlive(c.X,c.Z)));
        }
        Check(lowerBomb&&minimum<initial,"CPU ordinary planting and escape opens actual lower-level cover");
        var steering=new Fixture(stage,bots:1);steering.Step(62);
        steering.Session.AiTestPosition("cpu-0",16.5f,14.2f);
        var actor=steering.Session.AiTestPlayers.Single();int start=graph.Cell(actor.X,actor.Z,actor.Y);
        var probe=typeof(BlastSession).GetMethod("BotDirectionMoves",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Check(!(bool)probe.Invoke(steering.Session,[actor,start,0f,-1f])!,
            "CPU recovery rejects a stair sidestep that reaches water before the next reaction");
        Check((bool)probe.Invoke(steering.Session,[actor,start,-1f,0f])!,
            "CPU recovery retains a supported direction along the same stair");
        Console.WriteLine("PASS Up & Down layered CPU occupancy, danger, lower-cover combat and full-reaction stair recovery");return 5;
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
