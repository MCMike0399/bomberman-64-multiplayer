using System.Text.Json;
using Bomber.Game.Matches;
using static GameFixtures;

/// <summary>Cosmetic appearance validation, persistence and gameplay neutrality.</summary>
internal static class AppearanceChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("local appearances follow compact active roster for humans and CPU slots", () =>
        {
            var appearances=new[]{5,7,2};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=2,PlayerAppearances=appearances});
            appearances[0]=0;Step(match,2);
            Assert(State(match).GetProperty("players").EnumerateArray().Select(p=>p.GetProperty("appearance").GetInt32()).SequenceEqual(new[]{5,7,2}),"Selected appearances not assigned or caller mutated authority configuration.");
            foreach(var bad in new[]{new[]{1},new[]{0,1,8},new[]{0,-1,2}})
            {var rejected=false;try{_=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=2,PlayerAppearances=bad});}catch(ArgumentException){rejected=true;}Assert(rejected,"Malformed local appearance roster accepted.");}
            match.SetAppearance(0,4);Step(match,2);
            Assert(Player(match).GetProperty("appearance").GetInt32()==4&&Player(match,1).GetProperty("appearance").GetInt32()==7,"Local cosmetic change affected another participant.");
        });
        run("appearance messages reject malformed choices and cannot target another player", () =>
        {
            var f=new AppearanceFixture();f.Message(0,"{\"index\":6}");f.Step(2);
            foreach(var malformed in new[]{"{}","null","[]","{\"index\":-1}","{\"index\":8}","{\"index\":1.5}","{\"index\":1e100}","{\"index\":\"2\"}","{\"index\":null}","{\"index\":true}","{\"index\":[2]}"})
            {
                f.Message(0,malformed);f.Step(2);
                Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==6,"Malformed cosmetic message changed selection: "+malformed);
            }
            f.Message(0,"{\"index\":7,\"slot\":1,\"playerId\":\"fixture-1\"}");f.Step(2);
            var players=f.Snapshot.GetProperty("players");
            Assert(players[0].GetProperty("appearance").GetInt32()==7&&players[1].GetProperty("appearance").GetInt32()==1,"Client targeted another player's appearance.");
            using var data=JsonDocument.Parse("{\"index\":3}");
            f.Session.OnMessage(f,new Burbuja.Realtime.Abstractions.RealtimePlayer("unseated","Unknown",0,new Dictionary<string,string>()),new Burbuja.Realtime.Abstractions.InboundMessage("appearance",data.RootElement.Clone(),null,f.Now));f.Step(2);
            Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==7,"Unseated sender changed appearance.");
        });
        run("appearance survives connection restoration and round equipment reset", () =>
        {
            var f=new AppearanceFixture();f.Message(0,"{\"index\":5}");
            f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);f.Session.OnPlayerConnectionChanged(f,f.Players[0],true);f.Step(62);
            Assert(f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==5,"Reconnect lost selected appearance.");
            f.Message(0,"{\"x\":-1,\"z\":0}","input");f.Step(20);f.Message(0,"{\"x\":0,\"z\":0}","input");f.Step(94);
            Assert(f.Snapshot.GetProperty("round").GetInt32()==2&&f.Snapshot.GetProperty("players")[0].GetProperty("appearance").GetInt32()==5,"Round reset lost selected appearance.");
        });
        run("different appearances produce identical gameplay simulation", () =>
        {
            var a=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,PlayerAppearances=[1,3]});
            var b=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,PlayerAppearances=[7,6]});
            for(var tick=0;tick<120;tick++){var input=new PlayerInput(tick%40<20?.1f:-.1f,0,tick%60==0);a.SetInput(0,input);b.SetInput(0,input);a.Tick();b.Tick();}
            var left=System.Text.Json.Nodes.JsonNode.Parse(a.StateJson!)!;var right=System.Text.Json.Nodes.JsonNode.Parse(b.StateJson!)!;
            foreach(var root in new[]{left,right})foreach(var player in root["d"]!["players"]!.AsArray())player!.AsObject().Remove("appearance");
            Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(left,right),"Cosmetic selection changed game outcomes or physics.");
        });
        run("late lobby joins and reconnects receive existing authoritative appearances", () =>
        {
            var f=new AppearanceFixture(startMatch:false);f.Message(0,"{\"index\":6}");f.Sent.Clear();
            var late=new Burbuja.Realtime.Abstractions.RealtimePlayer("late-joiner","Late",2,new Dictionary<string,string>());
            f.Session.OnPlayerJoined(f,late);
            Assert(f.Sent.Any(m=>m.Recipient==late.PlayerId&&m.Type=="appearance"&&m.Data.GetProperty("playerId").GetString()=="fixture-0"&&m.Data.GetProperty("index").GetInt32()==6),"Late joiner cannot see host selection before match snapshots.");
            f.Sent.Clear();f.Session.OnPlayerConnectionChanged(f,f.Players[1],true);
            Assert(f.Sent.Any(m=>m.Recipient=="fixture-1"&&m.Type=="appearance"&&m.Data.GetProperty("playerId").GetString()=="fixture-0"&&m.Data.GetProperty("index").GetInt32()==6),"Reconnecting lobby player did not recover appearance roster.");
        });
    }
}
