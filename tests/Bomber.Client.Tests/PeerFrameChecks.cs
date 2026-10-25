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
        var castle=new LocalMatch(new MatchOptions{Stage="top-rules",HumanPlayers=2,BotPlayers=0});
        using var castleStage=JsonDocument.Parse(castle.StageJson!);
        Check(PeerFrameRules.Valid(castleStage.RootElement),"Top Rules peer stage accepts fractional stairs, underpasses and authored wall heights");
        void InvalidCastle(Action<JsonNode> change,string label)
        {
            var frame=JsonNode.Parse(castle.StageJson!)!;change(frame["d"]!);
            Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(frame)),label);
        }
        InvalidCastle(d=>d.AsObject().Remove("underpasses"),"reject castle without its lower-passage metadata");
        InvalidCastle(d=>d["underpasses"]![0]=19*17,"reject out-of-bounds underpass index");
        InvalidCastle(d=>d["underpasses"]![0]=0,"reject underpass on a solid wall");
        InvalidCastle(d=>d["cells"]![5]!["oh"]=-1,"reject negative castle wall height");
        InvalidCastle(d=>d["cells"]![5]!["oh"]=1e200,"reject non-finite castle wall height");
        InvalidCastle(d=>d["cells"]![5]!.AsObject().Remove("oh"),"reject castle wall with no authored crown");
        InvalidCastle(d=>d["cells"]![2*19+2]!["rf"]=1e200,"reject invalid fractional ramp height");
        InvalidCastle(d=>d["cells"]![2*19+2]!["rz"]=99,"reject unbounded stair direction");
        var invalid=JsonNode.Parse(match.StateJson!)!;
        invalid["d"]!["players"]![0]!["x"]=1e200;
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject non-finite render coordinates");
        invalid=JsonNode.Parse(match.StateJson!)!;invalid["d"]!["softBlocks"]="not-base64!";
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject malformed block bitset");
        invalid=JsonNode.Parse(match.StageJson!)!;invalid["d"]!["width"]=100000;
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject stage allocation abuse");
        invalid=JsonNode.Parse(match.StateJson!)!;invalid["d"]!["players"]![0]!.AsObject().Remove("alive");
        Check(!PeerFrameRules.Valid(JsonSerializer.SerializeToElement(invalid)),"reject missing required player fields");
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
