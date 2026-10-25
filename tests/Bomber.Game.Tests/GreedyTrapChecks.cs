using Bomber.Game;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using static GameFixtures;

internal static class GreedyTrapChecks
{
    public static void Run(Action<string,Action> check)
    {
        const string id="greedy-trap";
        check("Greedy Trap rejects the Blizzard-only numeric protocol",()=>
            Assert(!OnlineProtocol.Accepts(["5"]),"Incompatible Blizzard-only build passed Greedy Trap admission."));
        check("Greedy Trap is an enclosed three-story reward pit with six flights",()=>
        {
            var stage=BlastStages.Get(id);
            Assert(stage.Id==id,"Greedy Trap silently fell back to Sunken Temple.");
            Assert(!stage.RisingWater&&stage.Underpasses.Count()==0,"Dry pit acquired sea or suspended surfaces.");
            Assert(stage.At(2,2).Tier==2&&stage.At(5,9).Tier==1&&stage.At(9,9).Tier==0,"Three reference elevations were lost.");
            Assert(stage.Cells.Cast<CellDef>().Count(c=>c.Kind==CellKind.Ramp)==8,"Expected two two-cell upper flights and four inner flights.");
            Assert(stage.Cells.Cast<CellDef>().All(c=>!c.IsElimination),"Turquoise floor was mistaken for lethal water.");
            Assert(stage.Spawns.Length==8&&stage.Spawns.Distinct().Count()==8&&stage.Spawns.All(s=>stage.At(s.X,s.Z).Tier==2),"Eight separate upper-court starts required.");
        });
        check("Greedy Trap bottom has eight blocks and advanced density remains effective",()=>
        {
            foreach(int percent in new[]{0,50,100})
            {
                var m=new LocalMatch(new MatchOptions{Stage=id,HumanPlayers=4,BotPlayers=0,Advanced=new AdvancedRules{BlockPercent=percent}});
                var bits=Convert.FromBase64String(State(m).GetProperty("softBlocks").GetString()!);
                var stage=BlastStages.Get(id);int count=0;
                for(int z=0;z<stage.Height;z++)for(int x=0;x<stage.Width;x++)
                    if((bits[(z*stage.Width+x)/8]&(1<<((z*stage.Width+x)%8)))!=0)
                    {count++;Assert(stage.At(x,z).Tier==0&&x is >=7 and <=11&&z is >=7 and <=11,"Cover escaped the bottom reward chamber.");}
                Assert(count==8*percent/100,$"Expected {8*percent/100} bottom blocks; got {count}.");
            }
        });
        check("Greedy Trap ordinary movement descends and returns through north and south stairs",()=>
        {
            var m=new LocalMatch(new MatchOptions{Stage=id,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(m);
            Walk(m,9.5f,2.5f);Walk(m,9.5f,5.5f);
            Assert(Math.Abs(Player(m).GetProperty("y").GetSingle()-1)<.01f,"North flight missed middle ring.");
            Walk(m,5.5f,5.5f);Walk(m,5.5f,6.5f);Walk(m,7.5f,6.5f);
            Assert(Player(m).GetProperty("y").GetSingle()==0,"Inner northwest flight missed bottom.");
            Walk(m,11.5f,6.5f);Walk(m,13.5f,6.5f);
            Assert(Player(m).GetProperty("y").GetSingle()==1,"Inner northeast exit failed.");
            Walk(m,13.5f,12.5f);Walk(m,11.5f,12.5f);
            Assert(Player(m).GetProperty("y").GetSingle()==0,"Inner southeast flight failed.");
            Walk(m,7.5f,12.5f);Walk(m,5.5f,12.5f);
            Assert(Player(m).GetProperty("y").GetSingle()==1,"Inner southwest exit failed.");
            Walk(m,5.5f,13.5f);Walk(m,9.5f,13.5f);Walk(m,9.5f,16.5f);
            Assert(Player(m).GetProperty("y").GetSingle()==2,"South flight failed to regain safe upper court.");
        });
        check("Greedy Trap bot navigation connects all stories without climbing a cliff",()=>
        {
            var stage=BlastStages.Get(id);var visited=new HashSet<(int,int)>{stage.Spawns[0]};var queue=new Queue<(int X,int Z)>();queue.Enqueue(stage.Spawns[0]);
            while(queue.TryDequeue(out var p))foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {var next=(p.X+d.Item1,p.Z+d.Item2);if(stage.CanCross(p.X,p.Z,next.Item1,next.Item2)&&visited.Add(next))queue.Enqueue(next);}
            Assert(visited.Contains((9,9))&&stage.Spawns.All(visited.Contains),"CPU legal-edge graph disconnected the reward pit or an upper start.");
            Assert(!stage.CanCross(5,8,4,8),"Bare middle ledge became an uphill staircase.");
            _=NavigationGraph.For(stage);
        });
        check("Greedy Trap hurry compresses symmetrically and crushes upper-court occupants",()=>
        {
            var m=new LocalMatch(new MatchOptions{Stage=id,HumanPlayers=4,BotPlayers=0,RoundSeconds=60,Ghosts=false});
            Step(m,62+30*3);
            var state=State(m);
            Assert(state.GetProperty("closedLeft").GetInt32()>=3&&state.GetProperty("closedLeft").GetInt32()==state.GetProperty("closedRight").GetInt32(),"Side walls did not close together.");
            Assert(!Player(m).GetProperty("alive").GetBoolean(),"Closing wall failed to crush a corner start.");
            Assert(state.GetProperty("waterLevel").GetSingle()<0,"Dry arena started flooding.");
        });
    }
}
