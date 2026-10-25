using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task TraversalAndGhosts()
    {
        Event("traversal-input-policy",new {stage="sunken-temple",source="ordinary local match inputs",fixtures=false,stateMutation=false});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"temple ledge practice reaches fight",150);
        var layout=BlastStages.Get("sunken-temple");
        var cover=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool Clear(int x,int z)=>(cover[(z*layout.Width+x)/8]&(1<<((z*layout.Width+x)%8)))==0;
        var ledges=new[]{(X:16,Z:4,Direction:1),(X:8,Z:4,Direction:-1)};
        var available=ledges.Where(l=>Clear(l.X+l.Direction,l.Z)).ToArray();
        Check(available.Length>0,"stock temple has an uncovered crown ledge for ordinary traversal");
        var ledge=available[0];await GrappleWalk(0,ledge.X,ledge.Z);
        float upper=GrappleValue(GrapplePlayer(0),"y");
        Check(upper==4,"ordinary ramp route reaches the temple crown");
        Input(0,new PlayerInput(ledge.Direction,0));
        await WaitState(_=>GrapplePlayer(0).GetProperty("falling").GetBoolean(),"walking across crown edge starts real gravity fall",30);
        var falling=GrapplePlayer(0);
        var departure=State.GetProperty("events").EnumerateArray().Last(e=>e.GetProperty("kind").GetString()=="player-fall");
        Check(falling.GetProperty("airborne").GetBoolean()&&GrappleValue(falling,"y")<upper&&GrappleValue(falling,"y")>3,"fall publishes intermediate height between crown and lower terrace");
        float airX=GrappleValue(falling,"x");
        Input(0,new PlayerInput(ledge.Direction*.6f,0));await Frames(4);
        float correction=(GrappleValue(GrapplePlayer(0),"x")-airX)*ledge.Direction;
        Check(correction>.08f&&correction<.4f,"ordinary stick input makes a limited outward correction during fall");
        Input(0,default);await Capture("temple-ledge-gravity",0);
        await WaitState(_=>!GrapplePlayer(0).GetProperty("airborne").GetBoolean(),"fall lands on the real lower temple terrace",60);
        Check(GrappleValue(GrapplePlayer(0),"y")==3&&!GrapplePlayer(0).GetProperty("stunned").GetBoolean(),"ordinary ledge landing preserves controls without throw recovery stun");
        var landing=State.GetProperty("events").EnumerateArray().Last(e=>e.GetProperty("kind").GetString()=="player-land");
        int fallTicks=landing.GetProperty("tick").GetInt32()-departure.GetProperty("tick").GetInt32()+1;
        float fallTravel=(GrappleValue(landing,"x")-GrappleValue(departure,"x"))*ledge.Direction;
        Check(fallTicks<=10&&fallTravel<.8f,"one-tier ledge descent stays brief and cannot glide across multiple cells");
        Event("ledge-fall-measurement",new {fallTicks,fallTravel,source="accepted ordinary-input fall and landing events"});
        await Capture("temple-ledge-landed");
        Input(0,new PlayerInput(-ledge.Direction,0));await Frames(12);Input(0,default);await Frames(2);
        Check(GrappleValue(GrapplePlayer(0),"y")==3,"walking against the upper cliff face cannot instantly climb back up");
        Check((GrappleValue(GrapplePlayer(0),"x")-(ledge.Direction>0?17:8))*ledge.Direction>=-.01f,"upper cliff retains a solid face after landing");
        await Capture("temple-upward-cliff-blocked");

        int shoreX=Enumerable.Range(5,11).First(x=>Clear(x,24)&&Clear(x,25));
        await GrappleWalk(0,shoreX,25);
        Input(0,new PlayerInput(0,.35f));
        await WaitState(_=>GrappleValue(GrapplePlayer(0),"z")>=25.72f,"ordinary inputs reach the dry shoreline with body radius over water",40);
        Input(0,default);await Frames(8);
        var shore=GrapplePlayer(0);
        Check(GrappleValue(shore,"z")<26&&GrappleValue(shore,"y")==0&&!shore.GetProperty("airborne").GetBoolean(),"grazing the shore leaves the feet supported by real land");
        await Capture("temple-shore-grazing-dry");
        Input(0,new PlayerInput(0,.6f));
        await WaitState(_=>GrapplePlayer(0).GetProperty("falling").GetBoolean(),"crossing the low shore starts a real gravity fall",30);
        Input(0,default);shore=GrapplePlayer(0);
        Check(GrappleValue(shore,"y")<0&&GrappleValue(shore,"y")>State.GetProperty("waterLevel").GetSingle(),"low shoreline fall stays alive between dry land and the visible water plane");
        Check(Math.Abs(Visual.GetProperty("waterHeight").GetSingle()-State.GetProperty("waterLevel").GetSingle()*TempleStageView.HeightScale)<.01f,"shoreline contact plane matches the actual rendered water height");
        await Capture("temple-shore-falling-before-contact",0);
        await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="water"),"ordinary shoreline fall reaches water and produces a real death",90);
        var shoreDeath=State.GetProperty("events").EnumerateArray().Last(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="water");
        Check(Math.Abs(shoreDeath.GetProperty("y").GetSingle()-State.GetProperty("waterLevel").GetSingle())<.001f,"shoreline death is accepted exactly at water contact height");
        await Capture("temple-shore-actual-water-contact");
        _root.Back();await Frames(3);

        foreach(int seat in new[]{0,1,2})Input(seat,default);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),3,practice:false);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"three-human real match reaches fight for ghost interaction",150);
        await GrappleWalk(0,12,13);await GrappleWalk(1,15,13);
        Input(0,new PlayerInput(0,1));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
        await WaitState(_=>GrapplePlayer(0).GetProperty("ghost").GetBoolean(),"real fuse explosion produces a live ghost in an ordinary match",120);
        Check(GrapplePlayer(0).GetProperty("alive").GetBoolean()&&GrapplePlayer(1).GetProperty("alive").GetBoolean()&&!GrapplePlayer(1).GetProperty("ghost").GetBoolean(),"ghost death preserves the living target and active multiplayer round");
        Input(0,new PlayerInput(1,0));
        await WaitState(_=>GrapplePlayer(0).GetProperty("latchedTo").ValueKind==JsonValueKind.String,"ghost approaches and latches through real movement inputs",65);
        string host=GrapplePlayer(1).GetProperty("id").GetString()!;
        Check(GrapplePlayer(0).GetProperty("latchedTo").GetString()==host,"ghost snapshot identifies its actual living host");
        float livingX=GrappleValue(GrapplePlayer(1),"x");
        Input(0,new PlayerInput(-1,0));Input(1,new PlayerInput(1,0));await Frames(10);
        Check(GrappleValue(GrapplePlayer(1),"x")>livingX+.2f,"living player's own movement wins against opposed ghost steering");
        Check(Math.Abs(GrappleValue(GrapplePlayer(0),"x")-GrappleValue(GrapplePlayer(1),"x"))<.02f&&
            Math.Abs(GrappleValue(GrapplePlayer(0),"z")-GrappleValue(GrapplePlayer(1),"z"))<.02f,"latched ghost authority follows final host movement");
        Input(0,default);Input(1,default);await Frames(4);
        var ghostModel=TraversalModel(0);var hostModel=TraversalModel(1);var relative=ghostModel.Position-hostModel.Position;
        Check(Math.Abs(relative.X)<.03f&&Math.Abs(relative.Z)<.03f&&Math.Abs(relative.Y-1.5f)<.03f,"actual ghost mesh rides visibly above the interpolated living model");
        await Capture("ghost-rides-living-player");
        await WaitState(_=>GrapplePlayer(0).GetProperty("latchedTo").ValueKind==JsonValueKind.Null,"ghost latch expires and restores independent controls",260);
        Check(State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="ghost-detach"),"finite ghost detach is recorded by the authority");
        await Capture("ghost-detached");
        _root.Back();await Frames(3);
    }

    private BomberModel TraversalModel(int seat)=>_root.FindChildren("*","",true,false).OfType<BomberModel>()
        .Single(m=>m.IsVisibleInTree()&&m.FindChildren("*","",true,false).OfType<Label3D>().Any(label=>label.Text==$"P{seat+1}"));
}
