using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using static GameFixtures;

/// <summary>Presentation events recorded only for accepted authority actions.</summary>
internal static class PresentationEventChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("presentation events record only accepted held actions and one authoritative blast", () =>
        {
            var match = new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)],15)); Fight(match);
            match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);
            for(var i=0;i<8;i++){match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);match.SetInput(0,new PlayerInput(0,0,true,true));Step(match,2);}
            Step(match,30);
            var events=Events(match);
            Assert(events.Count(e=>Kind(e)=="bomb-carry")==1&&!events.Any(e=>Kind(e)=="bomb-drop"),"Held creation reported a ground placement.");
            Assert(events.Count(e=>Kind(e)=="pump")==3,"Rejected capped pumps emitted events.");
            match.SetInput(0,default);Step(match,2);
            var thrown=Events(match).Single(e=>Kind(e)=="throw");
            Assert(thrown.GetProperty("playerId").GetString()=="local-0"&&thrown.GetProperty("radius").GetSingle()>3,"Throw lost actor or effective radius.");
            Step(match,100);events=Events(match);
            Assert(events.Count(e=>Kind(e)=="throw")==1&&events.Count(e=>Kind(e)=="blast")==1,"One action emitted duplicate throw/blast events.");
            Assert(events.Single(e=>Kind(e)=="blast").GetProperty("entityId").GetString()==thrown.GetProperty("entityId").GetString(),"Blast lost bomb identity.");
        });
        run("walking back into a bomb kicks across the blocked final movement step", () =>
        {
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
            match.SetInput(0,new PlayerInput(-.2f,0));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
            var bombX=State(match).GetProperty("bombs")[0].GetProperty("x").GetSingle();
            match.SetInput(0,new PlayerInput(.95f,0));Step(match,14);
            Assert(Player(match).GetProperty("x").GetSingle()>bombX+1.8f,"Repro did not move away from front bomb.");
            Assert(!Events(match).Any(e=>Kind(e)=="kick"),"Walking away kicked the bomb behind the player.");
            match.SetInput(0,new PlayerInput(-1,0));Step(match,2);
            Assert(!Events(match).Any(e=>Kind(e)=="kick"),"Bomb was kicked before this tick could reach contact.");
            for(int i=0;i<16&&!Events(match).Any(e=>Kind(e)=="kick");i++)match.Tick();
            Assert(Events(match).Any(e=>Kind(e)=="kick"),"Movement collision stopped the player outside the fixed kick distance.");
            Assert(State(match).GetProperty("bombs")[0].GetProperty("x").GetSingle()<bombX-.05f,"Accepted kick did not roll the actual bomb.");
        });
        run("presentation ground placement lift and repeated blocked contact are distinct", () =>
        {
            var arena=FlatArena([(3,3)]);
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
            Assert(Events(match).Count(e=>Kind(e)=="bomb-drop")==1&&Events(match).Count(e=>Kind(e)=="lift")==1,"Accepted ground placement/lift missing.");
            arena=FlatArena([(3,3)]);arena.Cells[3,6]=CellDef.Pillar;
            match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,2);
            match.SetInput(0,new PlayerInput(0,.4f,A:true));
            for(int tick=0;tick<50&&Events(match).Count(e=>Kind(e)=="kick")<2;tick++)match.Tick();
            var kicks=Events(match).Count(e=>Kind(e)=="kick");
            Assert(kicks==2,"Expected initial roll and a fresh contact after the fast bomb rolled away. "+State(match));
            Step(match,12);
            Assert(Events(match).Count(e=>Kind(e)=="kick")==kicks,"Continuous blocked contact emitted duplicate kicks.");
        });
        run("presentation deaths retain explosion water and fall reasons through practice recovery", () =>
        {
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,88);
            var death=Events(match).Single(e=>Kind(e)=="death");
            Assert(death.GetProperty("reason").GetString()=="explosion"&&death.GetProperty("causedBy").GetString()=="local-0","Explosion death reason/owner lost.");
            Assert(Events(match).Single(e=>Kind(e)=="respawn").GetProperty("reason").GetString()=="explosion","Practice recovery lost original cause.");
            foreach(var kind in new[]{CellKind.Water,CellKind.Void})
            {
                var stage=FlatArena([(1,3)]);for(var z=0;z<9;z++)stage.Cells[0,z]=kind==CellKind.Water?CellDef.Water:CellDef.Void;
                match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},stage);Fight(match);
                match.SetInput(0,new PlayerInput(-1,0));Step(match,6);
                // Crossing the edge begins a real fall; wait for contact below the floor.
                match.SetInput(0,default);Step(match,20);
                death=Events(match).First(e=>Kind(e)=="death");
                Assert(death.GetProperty("reason").GetString()==(kind==CellKind.Water?"water":"fall"),"Terrain death has incorrect cause.");
            }
        });
        run("presentation cursors survive resets and reconnects; retained history stays recent and bounded", () =>
        {
            var f=new AppearanceFixture();f.Step(62);var before=f.Snapshot.GetProperty("presentationSeq").GetInt64();
            f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);f.Session.OnPlayerConnectionChanged(f,f.Players[0],true);f.Step(2);
            Assert(f.Snapshot.GetProperty("presentationSeq").GetInt64()==before,"Reconnect reset or invented events.");
            f.Step(3800);
            var events=f.Snapshot.GetProperty("events").EnumerateArray().ToArray();
            Assert(events.Any(e=>e.GetProperty("round").GetInt32()==2)&&f.Snapshot.GetProperty("presentationSeq").GetInt64()>before,"Round reset lost fresh events or reset the sequence.");
            Assert(events.All(e=>e.GetProperty("round").GetInt32()==2),"Long-expired round-one effects are still transmitted.");
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
            for(var i=0;i<50;i++){match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,88);match.SetInput(0,default);Step(match,2);}
            events=Events(match);
            Assert(events.Length is >0 and <=128&&events[0].GetProperty("seq").GetInt64()>1,"Presentation history is empty or unbounded.");
            Assert(events.All(e=>e.GetProperty("tick").GetInt32()>=match.TickNumber-120),"Snapshot retained effects outside its four-second window.");
            for(var i=1;i<events.Length;i++)Assert(events[i].GetProperty("seq").GetInt64()==events[i-1].GetProperty("seq").GetInt64()+1,"Event sequence has gaps/duplicates.");
            Assert(events[^1].GetProperty("seq").GetInt64()==State(match).GetProperty("presentationSeq").GetInt64(),"Snapshot cursor disagrees with retained tail.");
        });
    }
}
