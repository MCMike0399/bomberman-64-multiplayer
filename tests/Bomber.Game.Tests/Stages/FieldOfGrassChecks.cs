using System.Reflection;
using Bomber.Game.Simulation;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

internal static class FieldOfGrassChecks
{
    public static void Run(Action<string,Action> run)
    {
        run("Field of Grass has a continuous valley between two sloping banks",()=>
        {
            var stage=BlastStages.Get(BlastStages.FieldOfGrass);
            Assert(stage.Width==21&&stage.Height==19&&stage.Spawns.Distinct().Count()==8,"Eight-seat meadow dimensions changed.");
            for(int x=1;x<20;x++)for(int z=1;z<18;z++)
                Assert(stage.At(x,z).IsPassable,"The original open valley gained an interior pillar.");
            for(int z=1;z<18;z++)for(int x=1;x<19;x++)
                Assert(stage.CanCross(x,z,x+1,z),"Slope has a gap or a vertical step.");
            Assert(Enumerable.Range(1,19).Count(x=>stage.At(x,9).Kind==CellKind.Floor&&stage.At(x,9).Tier==0)==11,"Reference meadow must occupy about sixty percent of the interior width.");
            Assert(stage.SurfaceAt(2.5f,9.5f)==2&&stage.SurfaceAt(18.5f,9.5f)==2&&stage.SurfaceAt(10.5f,9.5f)==0,"Side banks must overlook the meadow.");
            Assert(stage.Spawns.All(s=>stage.SurfaceAt(s.X+.5f,s.Z+.5f)==2),"All eight players must start on visible bank ledges.");
        });
        run("Field of Grass ordinary walking descends and returns up both banks",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.FieldOfGrass,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(match);
            Walk(match,2.5f,9.5f);Walk(match,10.5f,9.5f);
            Assert(Math.Abs(Player(match).GetProperty("y").GetSingle())<.01f,"Player did not reach grassy valley floor.");
            Walk(match,18.5f,9.5f);
            Assert(Math.Abs(Player(match).GetProperty("y").GetSingle()-2)<.01f,"Player could not climb opposite continuous slope.");
            match.SetInput(0,new(1,0));Step(match,90);match.SetInput(0,default);
            Assert(Player(match).GetProperty("x").GetSingle()<20&&Player(match).GetProperty("alive").GetBoolean(),"Enclosing bank fence must stop movement.");
        });
        run("Field of Grass seeded breakable rocks preserve clear crossings and scale with rules",()=>
        {
            var layouts=new HashSet<string>();
            foreach(int seed in Enumerable.Range(0,16))foreach(int percent in new[]{0,50,100})
            {
                var options=new MatchOptions{Stage=BlastStages.FieldOfGrass,HumanPlayers=8,BotPlayers=0,Seed=seed,Advanced=new(){BlockPercent=percent}};
                var match=new LocalMatch(options);var encoded=State(match).GetProperty("softBlocks").GetString()!;
                Assert(encoded==State(new LocalMatch(options)).GetProperty("softBlocks").GetString(),"Seeded meadow rock layout diverged.");
                var bits=Convert.FromBase64String(encoded);
                Assert(bits.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==28*percent/100,"Meadow cover ignored the advanced density setting.");
                for(int z=0;z<19;z++)for(int x=0;x<21;x++)
                    if((bits[(z*21+x)/8]&(1<<((z*21+x)%8)))!=0)
                        Assert(x is >=6 and <=14&&z!=9,"Rocks left the valley, blocked bank approaches or central crossing.");
                if(percent==100)layouts.Add(encoded);
            }
            Assert(layouts.Count>=12,"Meadow rocks must vary across new seeds.");
            var stage=BlastStages.Get(BlastStages.FieldOfGrass);
            Assert(BlastGeometry.Contains(stage,new SoftBlockGrid(21,19),9.5f,0,9.5f,3,11.5f,0,9.5f,false),"Decorative grass must never shield a real explosion.");
        });
        run("Field of Grass falling fireballs strike raised banks with matching CPU danger",()=>
        {
            var options=new MatchOptions{Stage=BlastStages.FieldOfGrass,HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Ghosts=false};
            var match=new LocalMatch(options);Step(match,64);
            var state=State(match);var warning=state.GetProperty("meteors")[0];
            Assert(state.GetProperty("hurryMode").GetString()=="meteors"&&state.GetProperty("closedLeft").GetInt32()==0,"Meadow must rain fireballs instead of closing walls.");
            float x=warning.GetProperty("x").GetSingle(),z=warning.GetProperty("z").GetSingle();
            var stage=BlastStages.Get(BlastStages.FieldOfGrass);var graph=NavigationGraph.For(stage);var danger=new TimedDangerField(graph,stage);
            danger.Build(new SoftBlockGrid(21,19),[],[],[new(){Id="bank-warning",X=x,Z=z,Remaining=1.2f}]);
            Assert(!danger.Safe(graph.Cell(x,z,2),1.1f,1.3f),"CPU danger missed elevated fireball target.");
            Step(match,38);
            Assert(!Player(match).GetProperty("alive").GetBoolean(),"Stationary bank player survived a published meteor impact.");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var session=(BlastSession)typeof(LocalMatch).GetField("_session",flags)!.GetValue(match)!;
            var hazards=(List<BlastHazard>)typeof(BlastSession).GetField("_hazards",flags)!.GetValue(session)!;
            Assert(hazards.Any(h=>h.Tier==2&&Math.Abs(h.Y-2)<.01f&&Math.Abs(h.X-x)<.01f&&Math.Abs(h.Z-z)<.01f),
                "The real elevated meteor hazard must retain both its exact height and bank tier.");
            Assert(Events(match).Any(e=>Kind(e)=="blast"&&Math.Abs(e.GetProperty("y").GetSingle()-2)<.01f),"Meteor blast did not use the actual bank elevation.");
            foreach(var (practice,sudden) in new[]{(false,false),(true,true)})
            {
                var disabled=new LocalMatch(options with{Practice=practice,SuddenDeath=sudden});Step(disabled,180);
                Assert(State(disabled).GetProperty("meteors").GetArrayLength()==0,"Disabled sudden death or practice spawned fireballs.");
            }
        });
    }
}
