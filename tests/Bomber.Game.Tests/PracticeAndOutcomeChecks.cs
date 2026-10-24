using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

/// <summary>Practice respawns and final match outcomes.</summary>
internal static class PracticeAndOutcomeChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("practice self-bomb respawns a living player who can place another bomb", () =>
        {
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true}); Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);match.SetInput(0,default);Step(match,90);
            Assert(Player(match).GetProperty("alive").GetBoolean()&&!Player(match).GetProperty("ghost").GetBoolean(),"Practice death left the player as an unusable ghost.");
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
            Assert(State(match).GetProperty("bombs").GetArrayLength()>0,"Respawned practice player cannot place another bomb.");
            Assert(!match.IsEnded&&State(match).GetProperty("round").GetInt32()==1,"Practice respawn ended or reset the match.");
        });
        run("practice water fall drops held bomb and respawns with working controls", () =>
        {
            var arena=FlatArena([(1,3)]);for(var z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true},arena);Fight(match);
            match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,false,true));Step(match,2);
            Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Fixture did not lift the bomb before falling.");
            match.SetInput(0,new PlayerInput(-1,0,false,true));Step(match,20);match.SetInput(0,default);Step(match,2);
            var player=Player(match);
            Assert(player.GetProperty("alive").GetBoolean()&&!player.GetProperty("ghost").GetBoolean(),"Water left practice player dead/ghost.");
            Assert(player.GetProperty("x").GetSingle()>=1&&player.GetProperty("carrying").ValueKind==JsonValueKind.Null,"Practice spawn remained in water or kept a carried entity.");
            Walk(match,2.5f,3.5f);match.SetInput(0,new PlayerInput(0,0,true));Step(match,2);
            Assert(State(match).GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("x").GetSingle()>2),"Practice actions failed after water respawn.");
        });
        run("bot match win retains the final scoreboard in the end envelope", () =>
        {
            var arena=FlatArena([(1,3),(7,3)]);for(var z=0;z<9;z++)arena.Cells[0,z]=CellDef.Water;
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=1,Wins=1},arena);Fight(match);
            string? ended=null;match.Message+=json=>{using var doc=JsonDocument.Parse(json);if(doc.RootElement.GetProperty("t").GetString()=="sys.ended")ended=json;};
            match.SetInput(0,new PlayerInput(-1,0));Step(match,20);
            Assert(match.IsEnded&&match.Result?.Reason=="bot_won","Water elimination did not produce the bot match win.");
            Assert(match.Result!.Winners.Count==0,"Bot victory invented a human winner.");
            Assert(ended is not null,"No match end envelope was emitted.");
            var data=Data(ended!);var details=data.GetProperty("details");
            Assert(details.ValueKind==JsonValueKind.Object,"Bot win discarded final scoreboard details.");
            var rows=details.GetProperty("scoreboard").EnumerateArray().ToArray();
            var bot=rows.Single(r=>r.GetProperty("bot").GetBoolean());
            Assert(bot.GetProperty("wins").GetInt32()==1&&bot.GetProperty("id").GetString()=="bot-0","Final bot point missing from scoreboard.");
            Assert(rows.Length==2&&rows.Single(r=>!r.GetProperty("bot").GetBoolean()).GetProperty("wins").GetInt32()==0,"Final human score corrupted.");
        });
    }
}
