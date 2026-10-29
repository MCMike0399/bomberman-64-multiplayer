using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using System.Text.Json;
namespace BlastCircuit.Testing;

/// <summary>Ordinary bombs and timeout exercise the actual HUD, audio and result transition.</summary>
public partial class PlaytestDriver
{
    private async Task RoundEnding()
    {
        _root.StartLocal(new SessionSettings(Bots:0,Wins:2,Ghosts:false,SuddenDeath:false),2);
        Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"round announcement fixture reaches fight",150);
        await SelfBomb(false,"round-win");
        await WaitState(s=>s.GetProperty("round").GetInt32()==2,"nonfinal announcement advances to next round",150);
        Check(Announcement().Text=="Get Ready!"&&!Detail().Text.Contains("wins",StringComparison.Ordinal),"new round clears the previous winning announcement");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"second round reaches fight",150);
        await SelfBomb(true,"match-win");
        for(int i=0;i<150&&_root.ScreenName!="Results";i++)await Frames(1);
        Check(_root.ScreenName=="Results","final announcement eventually opens Results");
        Check((_root.FindChild("Standings",true,false) as Label)!.Text.Contains("2 WINS",StringComparison.Ordinal),"Results retains both winning points");
        await Capture("finish-match-results");_root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Bots:0,Wins:1,Ghosts:false,SuddenDeath:false),2);
        Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"draw fixture reaches fight",150);
        int wins=_root.BattleAudio.Counts.GetValueOrDefault("round-win"),draws=_root.BattleAudio.Counts.GetValueOrDefault("round-draw");
        Input(0,new PlayerInput(0,0,A:true));Input(1,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==3,"simultaneous real bombs end in a draw",200);
        Check(Announcement().Text=="Draw!"&&Detail().Text=="No survivors · No point awarded","simultaneous deaths announce Draw without a winner");
        Check(_root.ScreenName=="Match"&&State.GetProperty("players").EnumerateArray().All(p=>p.GetProperty("wins").GetInt32()==0),"draw keeps arena visible and awards no point");
        Check(_root.BattleAudio.Counts.GetValueOrDefault("round-draw")==draws+1&&_root.BattleAudio.Counts.GetValueOrDefault("round-win")==wins,"draw plays its own cue once, without a victory fanfare");
        await Frames(12);await Capture("finish-no-survivors-draw");
        await WaitState(s=>s.GetProperty("round").GetInt32()==2,"draw restarts after its announcement",150);
        _root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Bots:0,Wins:1,Ghosts:false,SuddenDeath:false,RoundSeconds:60),2);
        Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==3,"ordinary clock expiry reaches announcement",2100);
        Check(Announcement().Text=="Time's Up!"&&Detail().Text=="Draw · No point awarded","timeout announces Time's Up and the draw");
        Check(_root.ScreenName=="Match"&&State.GetProperty("players").EnumerateArray().All(p=>p.GetProperty("wins").GetInt32()==0),"timeout keeps living players and unchanged scores in arena");
        await Frames(12);await Capture("finish-time-up-draw");
        _root.Back();await Frames(3);

        async Task SelfBomb(bool final,string label)
        {
            int cues=_root.BattleAudio.Counts.GetValueOrDefault("round-win");
            Input(0,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==3,"real self-elimination reaches "+label,200);
            Check(_root.ScreenName=="Match","finish stays in the arena before "+label+" results");
            var winner=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("alive").GetBoolean());
            var name=PlayerNames.Display(winner.GetProperty("name").GetString(),winner.GetProperty("slot").GetInt32());
            Check(Announcement().Text=="Game!"&&Detail().Text==$"{name} wins the {(final?"match":"round")}!","HUD names the accepted "+label+" winner");
            Check(_root.FindChildren("*","",true,false).OfType<BlastCircuit.UI.Hud.BattleHud>().Single().Visible,"finish HUD remains visible for "+label);
            await Frames(12);await Capture("finish-"+label);
            Input(1,new PlayerInput(1,0,A:true));
            await Frames(30);Input(1,default);
            Check(_root.ScreenName=="Match"&&State.GetProperty("phase").GetInt32()==3,"finish remains visible after 1.4 seconds for "+label);
            var still=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==winner.GetProperty("id").GetString());
            Check(still.GetProperty("x").GetSingle()==winner.GetProperty("x").GetSingle()&&State.GetProperty("bombs").GetArrayLength()==0,"movement and bomb inputs cannot alter finished "+label);
            Check(_root.BattleAudio.Counts.GetValueOrDefault("round-win")==cues+1,"repeated finish snapshots play one fanfare for "+label);
        }
        Label Announcement()=>(Label)_root.FindChild("RoundAnnouncement",true,false)!;
        Label Detail()=>(Label)_root.FindChild("RoundAnnouncementDetail",true,false)!;
    }
}
