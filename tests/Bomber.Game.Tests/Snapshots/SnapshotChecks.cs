using Bomber.Game.Matches;
using static GameFixtures;

/// <summary>Server lighting and presentation history carried by authoritative snapshots.</summary>
internal static class SnapshotChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("structured local messages retain exact legacy JSON and independent snapshot lifetimes", () =>
        {
            var match=new LocalMatch(new MatchOptions{HumanPlayers=1,BotPlayers=1});
            var initial=match.StateMessage!.Value;var original=initial.GetRawText();
            Assert(match.StateJson==original&&match.StageJson==match.StageMessage!.Value.GetRawText(),"Initial structured and text snapshots differ.");
            var structured=new List<System.Text.Json.JsonElement>();var text=new List<string>();
            match.StructuredMessage+=structured.Add;match.Message+=text.Add;
            match.RequestStage();
            for(int tick=0;tick<180;tick++){match.SetInput(0,new PlayerInput(1,0,tick%50==0));match.Tick();}
            Assert(structured.Count==text.Count&&structured.Count>80,"Message paths omitted a stage or state frame.");
            Assert(structured.Select(m=>m.GetRawText()).SequenceEqual(text),"Message paths differ in payload or order.");
            Assert(initial.GetRawText()==original&&match.StateJson!=original,"Later ticks mutated a retained snapshot or retained stale state.");
            Assert(ReferenceEquals(match.StateJson,match.StateJson),"Repeated legacy reads recreate the same string.");
        });
        run("soft cover reuses unchanged encoding and invalidates every real mutation", () =>
        {
            var grid=new Bomber.Game.Simulation.State.SoftBlockGrid(7,3);
            var empty=grid.ToBase64();
            Assert(ReferenceEquals(empty,grid.ToBase64()),"Unchanged snapshots allocate a new cover encoding.");
            grid.Set(6,2,true);var filled=grid.ToBase64();
            Assert(Convert.FromBase64String(filled).SequenceEqual(new byte[]{0,0,16}),"Cover encoding changed row-major bit order.");
            Assert(empty==Convert.ToBase64String(new byte[3]),"A later mutation modified an earlier snapshot.");
            grid.Set(6,2,true);
            Assert(ReferenceEquals(filled,grid.ToBase64()),"An idempotent write invalidates the cover encoding.");
            grid.Set(6,2,false);Assert(grid.ToBase64()==empty,"Removing cover retained a stale encoding.");
            grid.Set(0,0,true);Assert(grid.ToBase64()==Convert.ToBase64String(new byte[]{1,0,0}),"Restored cover is missing from the encoding.");
            foreach(var (x,z) in new[]{(-1,0),(7,0),(0,-1),(0,3),(int.MaxValue,int.MaxValue)})
            {
                Assert(!grid.IsAlive(x,z),"An out-of-bounds cover query aliases a valid cell.");
                bool rejected=false;try{grid.Set(x,z,true);}catch(IndexOutOfRangeException){rejected=true;}
                Assert(rejected,"An out-of-bounds cover write aliases a valid cell.");
            }
            for(int z=0;z<3;z++)for(int x=0;x<7;x++)grid.Set(x,z,true);
            Assert(grid.ToBase64()==Convert.ToBase64String(new byte[]{255,255,31}),"Non-byte-aligned grid has missing cells or nonzero padding.");
            for(int z=0;z<3;z++)for(int x=0;x<7;x++){Assert(grid.IsAlive(x,z),"Packed cover lost a cell.");grid.Set(x,z,false);}
            Assert(grid.ToBase64()==empty,"Cleared packed cover differs from a fresh grid.");
        });
        run("server lighting is stable through reconnect and advances with actual rounds", () =>
        {
            var f=new AppearanceFixture();
            var seen=new HashSet<string>();
            for(int round=1;round<=4;round++)
            {
                Assert(f.Snapshot.GetProperty("round").GetInt32()==round,"Lighting fixture did not reach the next round.");
                var lighting=f.Snapshot.GetProperty("stageLighting").GetString()!;seen.Add(lighting);
                f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);
                f.Session.OnPlayerConnectionChanged(f,f.Players[0],true);f.Step(62);
                f.Message(0,"{\"stageLighting\":\"night\"}","stageLighting");f.Step(2);
                Assert(f.Snapshot.GetProperty("stageLighting").GetString()==lighting,"Reconnect, ticking or a client changed server lighting.");
                if(round==4)break;
                f.Message(0,"{\"x\":-1,\"z\":0}","input");f.Step(20);
                f.Message(0,"{\"x\":0,\"z\":0}","input");f.Step(94);
            }
            Assert(seen.SetEquals(new[]{"morning","midday","afternoon","night"}),"Four rounds did not show all lighting variants.");
        });
        run("server lighting supports every seed and reaches late joiners", () =>
        {
            var seen=new HashSet<string>();
            foreach(int seed in new[]{int.MinValue,-1,0,1,2,3,int.MaxValue})
            {
                var match=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,Seed=seed});
                seen.Add(State(match).GetProperty("stageLighting").GetString()!);
            }
            Assert(seen.SetEquals(new[]{"morning","midday","afternoon","night"}),"Seed mapping omitted a lighting variant.");
            var f=new AppearanceFixture();var expected=f.Snapshot.GetProperty("stageLighting").GetString();
            var late=new Burbuja.Realtime.Abstractions.RealtimePlayer("late","Late",2,new Dictionary<string,string>());
            f.Session.OnPlayerJoined(f,late);f.Step(2);
            Assert(f.Snapshot.GetProperty("stageLighting").GetString()==expected&&f.Snapshot.GetProperty("players").GetArrayLength()==3,"Late join changed lighting or lacks it in the complete snapshot.");
        });
        run("presentation snapshots expire stale effects but retain recent actions", () =>
        {
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},FlatArena([(3,3)]));Fight(match);
            var sequence=State(match).GetProperty("presentationSeq").GetInt64();
            Step(match,150);
            Assert(Events(match).Length==0,"Idle snapshots keep retransmitting expired presentation history.");
            Assert(State(match).GetProperty("presentationSeq").GetInt64()==sequence,"Expiring history must not reset the presentation cursor.");
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);
            var placed=Events(match).Single(e=>Kind(e)=="bomb-drop").GetProperty("seq").GetInt64();
            Step(match,30);
            Assert(Events(match).Any(e=>e.GetProperty("seq").GetInt64()==placed),"A short delivery gap lost a fresh effect.");
            Step(match,95);
            Assert(!Events(match).Any(e=>e.GetProperty("seq").GetInt64()==placed),"Old effect outlived the recovery window.");
        });
    }
}
