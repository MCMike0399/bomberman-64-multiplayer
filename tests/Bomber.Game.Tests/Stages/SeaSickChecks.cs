using Bomber.Game.Matches;
using Bomber.Game.Online;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using System.Text.Json;
using static GameFixtures;

internal static class SeaSickChecks
{
    public static void Run(Action<string,Action> run)
    {
        run("Sea Sick preserves three posts, connected side galleries and eight dry starts",()=>
        {
            var stage=BlastStages.Get(BlastStages.SeaSick);
            Assert(stage.Id==BlastStages.SeaSick&&stage.Width==19&&stage.Height==19,"Sea Sick must resolve its own authored layout.");
            var posts=Enumerable.Range(1,17).SelectMany(x=>Enumerable.Range(1,17).Select(z=>(X:x,Z:z)))
                .Where(c=>stage.At(c.X,c.Z).Kind==CellKind.Pillar).ToHashSet();
            Assert(posts.SetEquals([(9,8),(5,11),(13,11)]),"Central mast and two forward posts differ from the reference composition.");
            Assert(stage.At(9,8).ObstacleHeight>stage.At(5,11).ObstacleHeight&&!stage.RisingWater,"Mast must be taller, and the ship does not flood.");
            Assert(stage.Spawns.Distinct().Count()==8&&stage.Spawns.All(s=>stage.At(s.X,s.Z).IsPassable),"Eight distinct dry starts required.");
            var reached=new HashSet<(int X,int Z)>{stage.Spawns[0]};var queue=new Queue<(int X,int Z)>();queue.Enqueue(stage.Spawns[0]);
            while(queue.TryDequeue(out var a))foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var b=(X:a.X+dx,Z:a.Z+dz);
                if(stage.CanCross(a.X,a.Z,b.X,b.Z)&&reached.Add(b))queue.Enqueue(b);
            }
            Assert(reached.Count==stage.Cells.Cast<CellDef>().Count(c=>c.IsPassable),"A gallery or deck region is disconnected from ordinary walking.");
        });
        run("Sea Sick ordinary inputs climb both side galleries and stay within the gunwales",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.SeaSick,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(match);
            Walk(match,4.5f,4.5f);Walk(match,1.5f,4.5f);
            Assert(Math.Abs(Player(match).GetProperty("y").GetSingle()-1)<.01f,"Port stairs did not reach the raised gallery.");
            Walk(match,1.5f,14.5f);Walk(match,4.5f,14.5f);Walk(match,14.5f,14.5f);Walk(match,17.5f,14.5f);
            Assert(Math.Abs(Player(match).GetProperty("y").GetSingle()-1)<.01f,"Starboard stairs did not reach the raised gallery.");
            match.SetInput(0,new PlayerInput(1,0));Step(match,45);match.SetInput(0,default);
            Assert(Player(match).GetProperty("x").GetSingle()<18&&Player(match).GetProperty("alive").GetBoolean(),"Visible gunwale allowed an ordinary walk off the ship.");
        });
        run("Sea Sick has no authoritative rocking force or spontaneous bomb drift",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.SeaSick,HumanPlayers=8,BotPlayers=0,SuddenDeath=false});Fight(match);
            var before=State(match).GetProperty("players").EnumerateArray().Select(p=>(p.GetProperty("x").GetSingle(),p.GetProperty("y").GetSingle(),p.GetProperty("z").GetSingle())).ToArray();
            Step(match,240);
            var after=State(match).GetProperty("players").EnumerateArray().Select(p=>(p.GetProperty("x").GetSingle(),p.GetProperty("y").GetSingle(),p.GetProperty("z").GetSingle())).ToArray();
            Assert(before.SequenceEqual(after),"Cosmetic ship motion changed accepted stationary positions.");
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,1);match.SetInput(0,default);Step(match,2);
            var bomb=State(match).GetProperty("bombs")[0];float x=bomb.GetProperty("x").GetSingle(),z=bomb.GetProperty("z").GetSingle();
            Step(match,24);bomb=State(match).GetProperty("bombs")[0];
            Assert(bomb.GetProperty("x").GetSingle()==x&&bomb.GetProperty("z").GetSingle()==z,"A resting deck bomb drifted without a kick or input.");
        });
        run("Sea Sick barrels respect advanced budgets and retain clear gallery routes",()=>
        {
            var stage=BlastStages.Get(BlastStages.SeaSick);
            foreach(int percent in new[]{0,50,100})foreach(int seed in new[]{64,9876})
            {
                var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=8,BotPlayers=0,Seed=seed,Advanced=new AdvancedRules{BlockPercent=percent}});
                byte[] bits=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                var cover=Enumerable.Range(0,361).Where(i=>(bits[i/8]&(1<<(i%8)))!=0).ToArray();
                Assert(cover.Length==48*percent/100,"Barrel budget differs from advanced settings.");
                Assert(cover.All(i=>stage.At(i%19,i/19).Kind==CellKind.Floor&&!stage.ClearCells.Contains((i%19,i/19))),"A barrel blocks a reserved route, post or stair.");
            }
            Assert(!BlastGeometry.Contains(stage,new(19,19),7.5f,0,8.5f,5,11.5f,0,8.5f,true),"The mast fails to shield a deck-level blast.");
        });
        run("Sea Sick publishes fireball Hurry instead of closing ship columns",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.SeaSick,HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Ghosts=false});Step(match,64);
            var state=State(match);Assert(state.GetProperty("hurryMode").GetString()=="meteors"&&state.GetProperty("closedLeft").GetInt32()==0,"Ship uses an invented wall-closing hazard.");
            Assert(state.GetProperty("meteors").GetArrayLength()>0,"Ship omitted its real falling-fireball warning.");
            match.SetInput(0,new PlayerInput(0,1));Step(match,26);match.SetInput(0,default);Step(match,14);
            Assert(Player(match).GetProperty("alive").GetBoolean()&&State(match).GetProperty("hazards").GetArrayLength()>0,"Ordinary motion could not escape the fixed fireball target.");
        });
        run("Sea Sick room edits accept the supported stage and reject unknown nautical ids",()=>
        {
            using var good=JsonDocument.Parse("{\"stage\":\"sea-sick\"}");using var bad=JsonDocument.Parse("{\"stage\":\"sea-sick-current\"}");
            var current=new MatchOptions().ToSettings();
            Assert(RoomRules.TryUpdate(current,good.RootElement,out var accepted)&&accepted["stage"]==BlastStages.SeaSick,"Room update dropped the supported ship stage.");
            Assert(!RoomRules.TryUpdate(current,bad.RootElement,out _),"Unknown stage escaped exact admission validation.");
        });
    }
}
