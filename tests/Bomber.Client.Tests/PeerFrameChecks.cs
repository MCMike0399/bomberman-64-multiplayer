using System.Text.Json;
using System.Text.Json.Nodes;
using Bomber.Client;
using Bomber.Game;

internal static class PeerFrameChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        var match=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=6});
        using var stage=JsonDocument.Parse(match.StageJson!);
        using var state=JsonDocument.Parse(match.StateJson!);
        Check(PeerFrameRules.Valid(stage.RootElement),"real authored peer stage accepted");
        Check(PeerFrameRules.Valid(state.RootElement),"real peer snapshot accepted");
        int valid=0;
        match.Message+=json=>{using var d=JsonDocument.Parse(json);if(d.RootElement.GetProperty("t").GetString()=="state"){Check(PeerFrameRules.Valid(d.RootElement),"actual bomb/bot snapshots satisfy peer boundary");valid++;}};
        for(int i=0;i<300;i++){match.SetInput(0,new PlayerInput(1,0,A:i%30==0));match.Tick();}
        Check(valid>100,"validator exercised live gameplay and presentation events");
        var invalid=JsonNode.Parse(match.StateJson!)!;
        invalid["d"]!["players"]![0]!["x"]=1e200;
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject non-finite render coordinates");
        invalid=JsonNode.Parse(match.StateJson!)!;invalid["d"]!["softBlocks"]="not-base64!";
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject malformed block bitset");
        invalid=JsonNode.Parse(match.StageJson!)!;invalid["d"]!["width"]=100000;
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject stage allocation abuse");
        invalid=JsonNode.Parse(match.StateJson!)!;invalid["d"]!["players"]![0]!.AsObject().Remove("alive");
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject missing required player fields");
        var garden=new LocalMatch(new MatchOptions{Stage="rock-garden",HumanPlayers=3,BotPlayers=0,RoundSeconds=60});
        for(int tick=0;tick<64;tick++)garden.Tick();
        using var meteorState=JsonDocument.Parse(garden.StateJson!);
        Check(meteorState.RootElement.GetProperty("d").GetProperty("meteors").GetArrayLength()>0&&PeerFrameRules.Valid(meteorState.RootElement),"real Rock Garden fireball warnings pass peer presentation boundary");
        invalid=JsonNode.Parse(garden.StateJson!)!;
        var meteor=invalid["d"]!["meteors"]![0]!.DeepClone();
        invalid["d"]!["meteors"]=new JsonArray(Enumerable.Range(0,9).Select(_=>meteor.DeepClone()).ToArray());
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject oversized fireball render allocation");
        foreach(var (field,value) in new[]{("remaining",-1d),("remaining",10d),("x",1e200)})
        {
            invalid=JsonNode.Parse(garden.StateJson!)!;invalid["d"]!["meteors"]![0]![field]=value;
            Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject malformed fireball warning "+field);
        }
        var duel=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Wins=1});
        for(int i=0;i<65;i++)duel.Tick();
        duel.SetInput(0,new PlayerInput(0,0,A:true));
        duel.Tick();duel.SetInput(0,default);
        for(int i=0;i<1000&&!duel.IsEnded;i++)duel.Tick();
        Check(duel.IsEnded&&PeerFrameRules.Valid(JsonSerializer.SerializeToElement(new{t="party.result",d=duel.Result},Burbuja.Realtime.Abstractions.RealtimeJson.Default)),"real completed match produces valid peer results");
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(new{t="party.result",d=new{reason="won",winners=new[]{"peer"},details=new{scoreboard="bad"}}})),"malformed peer scoreboard is rejected before UI");
        Console.WriteLine($"PASS {checks} peer frame boundary checks");return checks;
    }
}
