using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using static GameFixtures;

/// <summary>Real eliminations retain an arena announcement before advancing or ending a match.</summary>
internal static class RoundEndingChecks
{
    public static int Run()
    {
        int passed=0;
        foreach(bool cpu in new[]{false,true})foreach(int target in new[]{1,2})
        {
            var arena=FlatArena([(1,3),(7,3)]);
            for(int z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
            var match=new LocalMatch(new MatchOptions{HumanPlayers=cpu?1:2,BotPlayers=cpu?1:0,Wins=target,Ghosts=false,SuddenDeath=false},arena);
            Fight(match);
            int ends=0;long endedAt=0;
            match.Message+=json=>{using var doc=JsonDocument.Parse(json);if(doc.RootElement.GetProperty("t").GetString()=="sys.ended"){ends++;endedAt=match.TickNumber;}};
            match.SetInput(0,new PlayerInput(-1,0));
            for(int i=0;i<40&&State(match).GetProperty("phase").GetInt32()!=3;i++)match.Tick();
            var over=State(match);long stoppedAt=match.TickNumber;
            Assert(over.GetProperty("phase").GetInt32()==3,"Water elimination did not reach round over.");
            Assert(!match.IsEnded&&ends==0,"Match jumped to Results before the arena finish announcement.");
            var winner=over.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("alive").GetBoolean());
            Assert(winner.GetProperty("wins").GetInt32()==1,"Winning point was not visible during the announcement.");
            var finish=Events(match).Single(e=>Kind(e)=="round-over");
            Assert(finish.GetProperty("playerId").GetString()==winner.GetProperty("id").GetString()&&finish.GetProperty("reason").GetString()=="last_standing","Announcement identifies the wrong winner or reason.");
            match.SetInput(0,new PlayerInput(1,0,A:true));
            if(!cpu)match.SetInput(1,new PlayerInput(-1,0,A:true));
            Step(match,60);
            Assert(!match.IsEnded&&State(match).GetProperty("phase").GetInt32()==3,"Announcement did not remain visible for at least two seconds.");
            foreach(var p in State(match).GetProperty("players").EnumerateArray())
            {
                var before=over.GetProperty("players").EnumerateArray().Single(q=>q.GetProperty("id").GetString()==p.GetProperty("id").GetString());
                foreach(string field in new[]{"x","y","z","wins","alive"})Assert(p.GetProperty(field).GetRawText()==before.GetProperty(field).GetRawText(),"Gameplay continued during the announcement: "+field);
            }
            Assert(State(match).GetProperty("bombs").GetArrayLength()==0,"Finish inputs planted another bomb.");
            Step(match,36);
            if(target==1)
            {
                Assert(match.IsEnded&&ends==1&&endedAt-stoppedAt is >=88 and <=92,"Final result was not emitted exactly once after the three-second finish.");
                Assert(match.Result?.Reason==(cpu?"bot_won":"won"),"Final result changed winner type.");
                var details=JsonSerializer.SerializeToElement(match.Result!.Details);
                Assert(details.GetProperty("scoreboard").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==winner.GetProperty("id").GetString()).GetProperty("wins").GetInt32()==1,"Delayed results lost the final point.");
                Step(match,120);Assert(ends==1&&State(match).GetProperty("round").GetInt32()==1,"Finished match restarted or ended twice.");
            }
            else
            {
                Assert(!match.IsEnded&&ends==0&&State(match).GetProperty("round").GetInt32()==2,"Ordinary round failed to restart after its announcement.");
                Assert(State(match).GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==winner.GetProperty("id").GetString()).GetProperty("wins").GetInt32()==1,"Next round lost its score.");
            }
            passed++;Console.WriteLine($"PASS {(cpu?"CPU":"human")} {(target==1?"match":"round")} finish retains arena, score and frozen inputs");
        }
        var draw=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,RoundSeconds=60,SuddenDeath=false});Fight(draw);
        for(int i=0;i<1900&&State(draw).GetProperty("phase").GetInt32()!=3;i++)draw.Tick();
        Assert(!draw.IsEnded&&State(draw).GetProperty("phase").GetInt32()==3,"Timeout skipped the announcement.");
        var timeout=Events(draw).Single(e=>Kind(e)=="round-over");
        Assert(timeout.GetProperty("reason").GetString()=="time_up"&&timeout.GetProperty("playerId").ValueKind==JsonValueKind.Null,"Timeout invented a winner.");
        Step(draw,96);Assert(State(draw).GetProperty("round").GetInt32()==2&&State(draw).GetProperty("players").EnumerateArray().All(p=>p.GetProperty("wins").GetInt32()==0),"Draw changed the score or failed to restart.");
        passed++;Console.WriteLine("PASS timeout announces a draw before a fresh round");
        return passed;
    }
}
