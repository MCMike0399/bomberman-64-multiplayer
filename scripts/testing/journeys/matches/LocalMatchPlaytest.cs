using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using System.Text.Json;
namespace BlastCircuit.Testing;

/// <summary>Core local battles: accepted actions, ramps, items, pump growth, flood, combat and results.</summary>
public partial class PlaytestDriver
{
    private async Task LocalActions()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"practice reaches fight",150);
        Check(State.GetProperty("players").GetArrayLength()==1,"practice contains one human");
        await Capture("practice-start");
        Input(0,new PlayerInput(0,0,A:true)); await Frames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"bomb input creates authoritative bomb");
        Input(0,default); await Frames(4);
        Input(0,new PlayerInput(0,0,B:true)); await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","lift input carries bomb");
        Input(0,new PlayerInput(0,0,A:true,B:true)); await Frames(16);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()>=1,"pump input enlarges held bomb");
        await Capture("bomb-held-pumped");
        Input(0,new PlayerInput(1,0)); await Frames(4);
        Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null,"release lift throws bomb");
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3),"thrown bomb follows flight state");
        await Capture("bomb-flight");
        Input(0,default); _root.Back(); await Frames(4);
        Check(_root.ActiveSession==null,"leave practice disposes active session");
    }

    private async Task Ramps()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ramp practice reaches fight",150);
        Check(Math.Abs(Player.GetProperty("x").GetSingle()-12.5f)<.1,"temple spawn centered on staircase corridor");
        await MoveTo(12.5f,2.5f,200);
        Check(Player.GetProperty("y").GetSingle()>=5,"walked all four stair flights to crown tier5");
        await Capture("temple-crown");
        await MoveTo(12.5f,12.5f,200);
        Check(Player.GetProperty("y").GetSingle()<=1,"walked back down to base terrace tier1");
        await Capture("temple-descended");
        _root.Back(); await Frames(4);
    }

    private async Task Powerups()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"sparse sandbox reaches fight",150);
        var cache=State.GetProperty("items").EnumerateArray().ToArray();
        Check(cache.Length==6&&Visual.GetProperty("powerups").GetInt32()==6,"default sandbox renders exactly six pickups");
        Check(cache.Count(i=>i.GetProperty("type").GetString() is "fire" or "bomb")==3,"sandbox starts with three stacking upgrades");
        Check(cache.Count(i=>PowerUpRules.IsStatus(i.GetProperty("type").GetString()!)||PowerUpRules.IsAbility(i.GetProperty("type").GetString()!))==2,"sandbox starts with two buffs/debuffs");
        Check(cache.Count(i=>PowerUpRules.IsBombType(i.GetProperty("type").GetString()!))==1,"sandbox starts with one bomb type");
        await Capture("sparse-default-sandbox");
        _root.Back();await Frames(3);
        // A six-item explicit pool keeps this equipment-swap journey independent of the seeded cache choice.
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0){Advanced=new AdvancedRules{DisabledItems="bounce,focus,tiny,big,haste,slow"}},1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"training pickups reach fight",150);
        var initialCapacity=Player.GetProperty("bombs").GetInt32();
        Check(Visual.GetProperty("powerups").GetInt32()==6,"six training pickup models visible");
        await Capture("powerups-visible-cache");
        await MoveTo(10.5f,12.5f,80);
        Check(Player.GetProperty("fire").GetInt32()==2,"walking onto first flame increases fire level");
        await MoveTo(9.5f,12.5f,60);
        Check(Player.GetProperty("fire").GetInt32()==3,"second flame reaches blast stack 3");
        await MoveTo(14.5f,12.5f,120);
        Check(Player.GetProperty("bombs").GetInt32()==initialCapacity+1,"walking onto bomb increases capacity by exactly one");
        await MoveTo(14.5f,13.5f,60);
        Check(Player.GetProperty("red").GetBoolean()&&!Player.GetProperty("remote").GetBoolean(),"walking onto red power selects red bomb mode");
        await MoveTo(12.5f,13.5f,80);
        Check(Player.GetProperty("heart").GetBoolean(),"walking onto heart grants extra hit");
        await MoveTo(10.5f,13.5f,80);
        Check(Player.GetProperty("remote").GetBoolean()&&!Player.GetProperty("red").GetBoolean(),"walking onto remote replaces red with command detonation");
        Check(Player.GetProperty("pickupSerial").GetInt64()>=6,"six real pickups accepted by authority");
        Check(State.GetProperty("items").EnumerateArray().All(i=>i.GetProperty("dropped").GetBoolean() || i.GetProperty("type").GetString() is "bounce" or "focus" or "tiny" or "big" or "haste" or "slow"),"collected pickups disappear while new equipment and swap drops remain");
        await Capture("powerups-collected-hud");
        Input(0,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);await Frames(4);
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>!b.GetProperty("red").GetBoolean()&&b.GetProperty("remote").GetBoolean()),"latest remote pickup determines newly placed bomb mode");
        await Capture("powerups-remote-bomb");
        Input(0,new PlayerInput(0,0,R:true));await Frames(4);Input(0,default);await Frames(4);
        Check(State.GetProperty("hazards").GetArrayLength()>0,"remote action detonates upgraded bomb");
        // In-battle HUD buttons deliberately stay out of D-pad focus navigation.
        await MouseClick("RESET");await Frames(4);
        Check(Player.GetProperty("fire").GetInt32()==1&&Player.GetProperty("bombs").GetInt32()==initialCapacity,"practice reset restores baseline upgrades");
        Check(Player.GetProperty("pickupSerial").GetInt64()==0&&Visual.GetProperty("powerups").GetInt32()>=6,"practice reset restores visible training cache");
        await Capture("powerups-practice-reset");
        _root.Back();await Frames(3);
    }

    private async Task PumpGrowth()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"pump training reaches fight",150);
        Input(0,new PlayerInput(0,0,B:true));await Frames(2);
        Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","hold lift and press bomb creates carried bomb");
        Input(0,new PlayerInput(0,0,B:true));await Frames(12);
        float previous=Visual.GetProperty("largestBombScale").GetSingle();
        float baseScale=previous;
        await Capture("pump-stage-0");
        for(var charge=1;charge<=3;charge++)
        {
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(16);
            Check(Visual.GetProperty("pumpIntensity").GetSingle()>.1f,$"body squash pose is active during pump{charge}");
            await Capture($"pump-action-{charge}");
            Input(0,new PlayerInput(0,0,B:true));await Frames(12);
            Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==charge,$"accepted pump stage{charge}");
            var scale=Visual.GetProperty("largestBombScale").GetSingle();
            Check(scale<=2.3f,$"pumped bomb stays within readable size limit at stage{charge}");
            Check(scale>previous+.2f,$"rendered bomb grows at pump stage{charge}");previous=scale;
            Event("pump-visual",new {charge,scale,visual=Visual});
            await Capture($"pump-stage-{charge}");
        }
        Check(Visual.GetProperty("carriedBombs").GetInt32()==1,"charged model remains attached to carrier socket");
        Check(previous>baseScale*1.7f,"fully charged bomb stays visibly larger than its unpumped model");
        Check(Visual.GetProperty("pumpAnimations").GetInt32()>=3,"three accepted pumps trigger body animation");
        Check(Player.GetProperty("pumpSerial").GetInt64()>=3,"authority publishes three accepted pump events");
        Input(0,new PlayerInput(1,0));await Frames(4);
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3&&b.GetProperty("pumps").GetInt32()==3),"release throws fully charged bomb");
        await Capture("pump-charged-flight");
        _root.Back();await Frames(3);
    }

    private async Task HurryWater()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0,Wins:3,RoundSeconds:60,SuddenDeath:true,Ghosts:false),2);
        Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"one-minute temple round enters hurry",180);
        Check(State.GetProperty("hurryMode").GetString()=="water","temple sudden death selects water mode");
        float initial=Visual.GetProperty("waterHeight").GetSingle();
        await MoveTo(12.5f,2.5f,200);
        Check(Player.GetProperty("y").GetSingle()>=4,"human climbs to flood-safe crown using inputs");
        await Capture("hurry-water-highground");
        await WaitState(s=>s.GetProperty("waterLevel").GetSingle()>=.25f,"rising water submerges lowest court",3000);
        Check(Visual.GetProperty("waterHeight").GetSingle()>initial+.5f,"rendered water plane rises with authority");
        Check(State.GetProperty("closedLeft").GetInt32()==0&&State.GetProperty("closedRight").GetInt32()==0,"temple does not shrink by closing columns");
        await Capture("hurry-water-court-flooded");
        await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("slot").GetInt32()==1&&!p.GetProperty("alive").GetBoolean()),"rising water eliminates lower-terrace human",3000);
        Check(Player.GetProperty("alive").GetBoolean()&&Player.GetProperty("y").GetSingle()>=4,"highground human survives lower terrace flooding");
        Check(State.GetProperty("waterLevel").GetSingle()>=1,"drowning occurs after water reaches low terrace height");
        await Capture("hurry-water-low-out-high-safe");
        _root.Back();await Frames(3);
    }

    private async Task Combat()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:3,Wins:1,RoundSeconds:60,SuddenDeath:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"combat reaches fight",150);
        var bombs=new HashSet<string>();var hazards=new HashSet<string>();var items=new HashSet<string>();
        bool roundOver=false,eliminated=false;
        for(var frame=0;frame<2200;frame++)
        {
            await Frames(1);
            if(_root.LatestState is not {} state)continue;
            foreach(var b in state.GetProperty("bombs").EnumerateArray())bombs.Add(b.GetProperty("id").GetString()!);
            foreach(var h in state.GetProperty("hazards").EnumerateArray())hazards.Add(h.GetProperty("id").GetString()!);
            foreach(var item in state.GetProperty("items").EnumerateArray())items.Add(item.GetProperty("id").GetString()!);
            eliminated|=state.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean()||p.GetProperty("ghost").GetBoolean());
            roundOver|=state.GetProperty("phase").GetInt32()==3 || _root.ScreenName=="Results";
            if(bombs.Count>0 && hazards.Count>0 && items.Count>0 && roundOver)break;
        }
        Event("combat-observed",new {bombs=bombs.Count,explosions=hazards.Count,pickups=items.Count,roundOver,eliminated});
        Check(bombs.Count>0,"combat observes server simulated bot bombs");
        Check(hazards.Count>0,"combat observes live explosions");
        Check(items.Count>0,"combat observes spawned item pickups through renderer");
        Check(roundOver,"combat observes completed round or match results");
        await Capture("combat-pickups-and-round");
        _root.Back();await Frames(3);
    }

    private async Task Results()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:1,Wins:1,Ghosts:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"results duel reaches fight",150);
        Input(0,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);
        Check(State.GetProperty("bombs").GetArrayLength()>0,"results duel places real self-elimination bomb");
        for(var i=0;i<600 && _root.ScreenName!="Results";i++)await Frames(1);
        Check(_root.ScreenName=="Results","real explosion eliminates human and completes CPU-won match");
        Check(State.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("bot").GetBoolean()&&p.GetProperty("wins").GetInt32()>=1),"CPU receives match-winning point");
        Check((_root.FindChild("Standings",true,false) as Label)!.Text.Contains("1 WIN",StringComparison.Ordinal),"results UI displays preserved winning point");
        var winner=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("bot").GetBoolean());
        var preview=(_root.FindChild("WinnerPreview",true,false) as BlastCircuit.UI.Menu.CharacterPreview)!;
        Check(preview.Appearance==winner.GetProperty("appearance").GetInt32()&&preview.Celebrating,"results celebrate the actual winner's selected suit");
        await Frames(12);
        var replayButton=VisibleButton("Play Again");var modeButton=VisibleButton("Mode Select");
        replayButton.GrabFocus();await KeyPress(Key.D);Check(modeButton.HasFocus(),"D moves across Results actions");
        await KeyPress(Key.A);Check(replayButton.HasFocus(),"A returns to the Results replay action");
        await Capture("results-cpu-victory");
        await Click("PLAY AGAIN");await Frames(4);
        Check(_root.ScreenName=="Match" && _root.ActiveSession!=null,"results replay button starts fresh match");
        _root.Back();await Frames(3);
        Check(_root.ScreenName=="Modes" && _root.ActiveSession==null,"leave replay returns to mode selector");
    }
}
