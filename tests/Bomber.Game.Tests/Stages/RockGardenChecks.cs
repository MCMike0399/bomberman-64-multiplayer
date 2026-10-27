using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

internal static class RockGardenChecks
{
    public static void Run(Action<string,Action> run)
    {
        run("Rock Garden is an enclosed square with five cardinal columns",()=>
        {
            var stage=BlastStages.Get(BlastStages.RockGarden);
            Assert(stage.Width==19&&stage.Height==19,"Garden must be a square court.");
            var columns=new HashSet<(int,int)>{(9,3),(3,9),(9,9),(15,9),(9,15)};
            for(int x=0;x<19;x++)for(int z=0;z<19;z++)
                Assert(stage.At(x,z).Kind==(x is 0 or 18||z is 0 or 18||columns.Contains((x,z))?CellKind.Pillar:CellKind.Floor),"Garden floor/column/wall topology differs from its contract.");
            Assert(stage.At(9,9).ObstacleHeight>stage.At(3,9).ObstacleHeight,"Center column must be taller than the outer four.");
            Assert(stage.Spawns.Distinct().Count()==8&&stage.Spawns.All(s=>stage.At(s.X,s.Z).IsPassable),"Eight distinct supported starts required.");
        });
        run("Rock Garden inputs traverse the center and stop at every enclosing wall",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.RockGarden,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(match);
            Walk(match,5.5f,8.5f);Walk(match,8.5f,8.5f);Walk(match,8.5f,10.5f);Walk(match,13.5f,10.5f);
            foreach(var (x,z,dx,dz) in new[]{(17.4f,10.5f,1f,0f),(1.6f,10.5f,-1f,0f),(1.6f,1.6f,0f,-1f),(1.6f,17.4f,0f,1f)})
            {
                Walk(match,x,z);match.SetInput(0,new PlayerInput(dx,dz));Step(match,90);match.SetInput(0,default);
                var p=Player(match);Assert(p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean(),"Walking into garden boundary killed player.");
                Assert(p.GetProperty("x").GetSingle()>1&&p.GetProperty("x").GetSingle()<18&&p.GetProperty("z").GetSingle()>1&&p.GetProperty("z").GetSingle()<18,"Walked through garden enclosure.");
            }
        });
        run("Rock Garden sudden death publishes fixed fireball warnings and authoritative impacts",()=>
        {
            var options=new MatchOptions{Stage=BlastStages.RockGarden,HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Ghosts=false};
            var a=new LocalMatch(options);var b=new LocalMatch(options);Step(a,64);Step(b,64);
            var warning=State(a).GetProperty("meteors")[0];
            Assert(State(a).GetProperty("hurryMode").GetString()=="meteors"&&State(a).GetProperty("closedLeft").GetInt32()==0,"Garden still uses closing columns.");
            Assert(warning.GetRawText()==State(b).GetProperty("meteors")[0].GetRawText(),"Seeded meteor warnings diverged.");
            float x=warning.GetProperty("x").GetSingle(),z=warning.GetProperty("z").GetSingle();
            Assert(Player(a).GetProperty("alive").GetBoolean()&&warning.GetProperty("remaining").GetSingle()>1,"Fireball hit before its warning.");
            a.SetInput(0,new PlayerInput(1,0));Step(a,26);a.SetInput(0,default);Step(a,14);Step(b,40);
            Assert(Player(a).GetProperty("alive").GetBoolean(),"Player could not escape the published target.");
            Assert(!Player(b).GetProperty("alive").GetBoolean(),"Stationary player was not hit by real fireball impact.");
            Assert(State(b).GetProperty("hazards").EnumerateArray().Any(h=>Math.Abs(h.GetProperty("r").GetSingle()-BlastStages.MeteorBlastRadius)<.01f)&&BlastStages.MeteorBlastRadius>2,
                "Fireball impact must use the enlarged blast radius the warning ring shows.");
            Assert(Events(b).Any(e=>Kind(e)=="blast"&&Math.Abs(e.GetProperty("x").GetSingle()-x)<.01f&&Math.Abs(e.GetProperty("z").GetSingle()-z)<.01f),"Meteor impact failed to emit the real blast event at its warned position.");
            var graph=Bomber.Game.Simulation.Ai.NavigationGraph.For(BlastStages.Get(BlastStages.RockGarden));
            var danger=new Bomber.Game.Simulation.Ai.TimedDangerField(graph,BlastStages.Get(BlastStages.RockGarden));
            danger.Build(new SoftBlockGrid(19,19),[],[],[new BlastMeteor{Id="warning",X=x,Z=z,Remaining=1.2f}]);
            Assert(danger.Safe(graph.Cell(x,z),0,.5f)&&!danger.Safe(graph.Cell(x,z),1.1f,1.3f),"AI warning time differs from visible meteor impact.");
            for(int tick=0;tick<600&&State(b).GetProperty("round").GetInt32()==1;tick++)b.Tick();
            Assert(State(b).GetProperty("round").GetInt32()==2&&State(b).GetProperty("meteors").GetArrayLength()==0,"Round reset retained falling fireballs.");
            foreach(var (practice,sudden) in new[]{(false,false),(true,true)})
            {
                var disabled=new LocalMatch(options with {Practice=practice,SuddenDeath=sudden});Step(disabled,180);
                Assert(State(disabled).GetProperty("meteors").GetArrayLength()==0,"Disabled sudden death or practice spawned fireballs.");
            }
        });
        run("Rock Garden center column shields blasts and sparse cover respects advanced budget",()=>
        {
            var stage=BlastStages.Get(BlastStages.RockGarden);var cover=new SoftBlockGrid(19,19);
            Assert(!BlastGeometry.Contains(stage,cover,8.5f,0,9.5f,4,10.5f,0,9.5f,true),"Central column failed to shield a piercing blast.");
            foreach(int percent in new[]{0,50,100})
            {
                var match=new LocalMatch(new MatchOptions{Stage=BlastStages.RockGarden,HumanPlayers=8,BotPlayers=0,Advanced=new AdvancedRules{BlockPercent=percent}});
                var bits=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                Assert(bits.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==16*percent/100,"Sparse boulder count ignored advanced settings.");
                if(percent==100)
                {
                    int central=Enumerable.Range(0,19).SelectMany(x=>Enumerable.Range(0,19).Select(z=>(X:x,Z:z)))
                        .Count(c=>Math.Abs(c.X-9)+Math.Abs(c.Z-9)<=3&&(bits[(c.Z*19+c.X)/8]&(1<<((c.Z*19+c.X)%8)))!=0);
                    Assert(central>=3,"Boulders miss the reference's central paving.");
                }
            }
        });
    }
}
