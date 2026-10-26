using Bomber.Game.Matches;
using static GameFixtures;

/// <summary>Server lighting and presentation history carried by authoritative snapshots.</summary>
internal static class SnapshotChecks
{
    public static void Run(Action<string, Action> run)
    {
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
