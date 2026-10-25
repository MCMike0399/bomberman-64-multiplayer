using Bomber.Game;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using static GameFixtures;

/// <summary>Reference-stage routes exercised through ordinary authoritative inputs.</summary>
internal static class UpAndDownChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("Up & Down has a high open ring, three elevations and water gaps", () =>
        {
            var stage = BlastStages.Get(BlastStages.UpAndDown);
            Assert(stage.At(2, 2).Tier == 4, "Outer ring must be level with the crown.");
            Assert(stage.At(4, 10).Tier == 2, "Side piers must sit below the ring.");
            Assert(stage.At(10, 15).Tier == 0, "Central reward deck must be the lowest level.");
            Assert(stage.At(8, 4).Kind == CellKind.Water, "The filled terrace still closes the sea gap.");
            Assert(stage.Spawns.Length == 8 && stage.Spawns.All(s => stage.At(s.X, s.Z).Tier == 4), "All eight spawns belong on the upper ring.");
        });
        run("Up & Down inputs traverse all six flights and both real underpasses", () =>
        {
            var match=new LocalMatch(new MatchOptions {Stage=BlastStages.UpAndDown,HumanPlayers=1,BotPlayers=0,Practice=true});
            Fight(match);
            void Go(float x,float z,float y)
            {
                Walk(match,x,z);
                Assert(Math.Abs(Player(match).GetProperty("y").GetSingle()-y)<.04f,$"Wrong floor at {x},{z}: {Player(match)}");
                Assert(Player(match).GetProperty("alive").GetBoolean()&&!Player(match).GetProperty("airborne").GetBoolean(),"A connected route became a fall.");
            }
            Go(11.5f,2.5f,4);Go(11.5f,7.5f,4);
            Go(5.5f,7.5f,2);Go(5.5f,14.5f,2);Go(11.5f,14.5f,0);
            Go(18.5f,14.5f,2);Go(18.5f,22.5f,2);
            Go(18.5f,21.5f,2);Go(11.5f,21.5f,0);Go(11.5f,23.5f,0);
            Go(11.5f,14.5f,0);Go(5.5f,14.5f,2);Go(5.5f,21.5f,2);
            Go(11.5f,21.5f,0);Go(11.5f,14.5f,0);Go(18.5f,14.5f,2);
            Go(18.5f,7.5f,2);Go(11.5f,7.5f,4);Go(13.5f,9.5f,4);
            Go(13.5f,21.5f,4);Go(2.5f,21.5f,4);Go(2.5f,2.5f,4);
        });
        run("Up & Down CPU graph keeps bridge and underpass routes separate", () =>
        {
            var stage=BlastStages.Get(BlastStages.UpAndDown);var graph=NavigationGraph.For(stage);
            int upper=graph.Cell(18.5f,21.5f,4),lower=graph.Cell(18.5f,21.5f,2);
            Assert(upper!=lower&&graph.Height[upper]==4&&graph.Height[lower]==2,"Navigation merged two floors.");
            Assert(graph.Edges[lower*4]>=0&&graph.Edges[lower*4+3]>=0,"Pier underpass is a dead end for CPUs.");
            var seen=new HashSet<int>{upper};var queue=new Queue<int>();queue.Enqueue(upper);
            while(queue.TryDequeue(out int node))for(int d=0;d<4;d++)
            {int next=graph.Edges[node*4+d];if(next>=0&&seen.Add(next))queue.Enqueue(next);}
            Assert(Enumerable.Range(0,graph.Count).Where(n=>graph.Walkable[n]).All(seen.Contains),"A playable surface is absent from CPU routing.");
            Assert(graph.Z(lower)==21&&graph.X(lower)==18,"Underpass node lost its planar coordinates.");
        });
        run("Up & Down bridge slab blocks rising bombs and separates blast levels", () =>
        {
            var stage=BlastStages.Get(BlastStages.UpAndDown);var cover=new SoftBlockGrid(stage.Width,stage.Height);
            Assert(stage.Ceiling(18.5f,21.5f,.3f,2.9f,3.6f,.61f)<3.3f,"Bomb passed upward through the bridge underside.");
            Assert(!BlastGeometry.Contains(stage,cover,18.5f,2,21.5f,4,18.5f,4,21.5f,true),"Blast passed through a solid bridge slab.");
            Assert(BlastGeometry.Contains(stage,cover,18.5f,2,20.5f,2,18.5f,2,22.5f,true),"Bridge became an invisible wall beneath its deck.");
            var match=new LocalMatch(new MatchOptions {Stage=BlastStages.UpAndDown,HumanPlayers=1,BotPlayers=0,Practice=true});
            Fight(match);Walk(match,11.5f,2.5f);Walk(match,11.5f,7.5f);Walk(match,18.5f,7.5f);Walk(match,18.5f,21.5f);
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,default);Step(match,2);
            var bomb=State(match).GetProperty("bombs")[0];
            Assert(bomb.GetProperty("y").GetSingle()==2,"A bomb placed beneath the ring teleported onto it.");
        });
        run("Up & Down complete stage message retains underpasses and fractional flights", () =>
        {
            var stage=BlastStages.Get(BlastStages.UpAndDown);
            var match=new LocalMatch(new MatchOptions {Stage=stage.Id});
            var message=Data(match.StageJson!);var cells=message.GetProperty("cells");
            Assert(cells[21*stage.Width+18].GetProperty("below").GetProperty("t").GetInt32()==2,"Network stage omitted the pier underpass.");
            Assert(Math.Abs(cells[7*stage.Width+8].GetProperty("rf").GetSingle()-8f/3)<.0001f,"Network stage rounded a long ramp to an integer stair.");
            match.RequestStage();
            Assert(Data(match.StageJson!).GetProperty("cells").GetRawText()==cells.GetRawText(),"A late stage request lost layered geometry.");
        });
        run("Up & Down shared throw projection meets the underside and lands below", () =>
        {
            var stage=BlastStages.Get(BlastStages.UpAndDown);
            var projection=BombThrowRules.Project(new(18.5f,2+BombThrowRules.LaunchHeight,21.5f),
                BombThrowRules.AimedVelocity(0,1,0),(x,z)=>stage.SurfaceAt(x,z),_=>false,
                cellKind:(x,z)=>stage.At(x,z).Kind,hasCover:(_,_)=>false,waterLevel:-1,stage:stage);
            Assert(projection.End==ThrowProjectionEnd.Landing&&Math.Abs(projection.Last.Y-2)<.001f,"Underpass advisory landed on the upper ring.");
            Assert(projection.Points.Max(p=>p.Y)+BombThrowRules.CoverHeight<=4-CellDef.DeckThickness+.002f,"Advisory passed through the underside.");
        });
        run("Up & Down flood eliminates low then middle terrain and leaves the ring", () =>
        {
            var stage=BlastStages.Get(BlastStages.UpAndDown);
            // Isolated lifecycle fixture, using the real terrain and explicit starting levels.
            var fixture=new StageLayout {Id=stage.Id,Width=stage.Width,Height=stage.Height,Cells=stage.Cells,
                Spawns=[(11,16),(5,17),(2,2)],RisingWater=stage.RisingWater,SoftBlockDensity=0};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,RoundSeconds=60},fixture);
            Step(match,62);
            Assert(State(match).GetProperty("hurryMode").GetString()=="water","Wrong stage hazard.");
            Step(match,280);
            Assert(!Player(match).GetProperty("alive").GetBoolean()&&Player(match,1).GetProperty("alive").GetBoolean(),"Flood did not separate bottom and middle levels.");
            Step(match,720);
            Assert(!Player(match,1).GetProperty("alive").GetBoolean()&&Player(match,2).GetProperty("alive").GetBoolean(),"Flood did not separate middle and upper levels.");
            Assert(State(match).GetProperty("closedLeft").GetInt32()==0,"Flood also closed columns.");
        });
    }
}
