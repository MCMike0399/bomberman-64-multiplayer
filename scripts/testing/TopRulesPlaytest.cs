using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task TopRulesTour()
    {
        Event("top-rules-policy", new { ordinaryInput = true, stateMutation = false, cameras = "normal battle", stage = "top-rules" });
        if (_root.ActiveSession != null) { _root.Back(); await Frames(3); }
        _root.Navigate("Setup"); await Frames(8);
        var state = _root.FindChild("SetupSettings", true, false)!;
        var menu = (ArcadeMenu)state.GetParent().GetParent().GetParent();
        menu.Configure(new SessionSettings(), 1);
        NamedButton("StageSelector").GrabFocus();
        for (int i = 0; i < 3; i++) await KeyPress(Key.Right);
        Check(menu.Settings.Stage == BlastStages.TopRules, "native arena dial reaches Top Rules");
        await Capture("top-rules-menu-miniature");
        _root.StartLocal(new SessionSettings(Stage: BlastStages.TopRules, Bots: 0), 4);
        await Frames(8);
        Check(State.GetProperty("players").GetArrayLength() == 4, "four real players start on the outer bridge ends");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"four-player castle opening reaches fight",150);
        await Frames(20);
        await Capture("top-rules-four-player-opening");
        var originalQuality=_root.Preferences.Graphics;
        await SelectGraphics(GraphicsPreset.Low);
        _root.StartLocal(new SessionSettings(Stage:BlastStages.TopRules,Bots:0,RoundSeconds:60),4);
        await Frames(110);
        var lowStone=AuditDescendants(LightingArena).OfType<MeshInstance3D>().Select(n=>n.MaterialOverride)
            .OfType<ShaderMaterial>().Where(m=>m.Shader.Code.Contains("uniform float quality_detail")).Distinct().ToArray();
        Check(lowStone.Length>0&&lowStone.All(m=>m.GetShaderParameter("quality_detail").AsSingle()==0),"restarted castle cover and closing walls retain Low shader detail");
        Check(lowStone.Length<=3,"castle rounds reuse bounded terrain-owned stone materials");
        await SelectGraphics(originalQuality);
        _root.Back(); await Frames(3);
        _root.Navigate("Practice"); await Frames(3);
        await ActivateNamed("LaunchMatch");
        await WaitState(s => s.GetProperty("phase").GetInt32() == 1, "ordinary menu launch starts Top Rules practice", 150);
        Check(_root.LatestStage!.Value.GetProperty("id").GetString() == BlastStages.TopRules, "practice retains the selected castle");
        var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
        var foundation=terrain.GetNode<MeshInstance3D>("CastleFoundation");
        var court=terrain.GetNode<MeshInstance3D>("CastleCourt");
        Check(foundation.Position.Y+foundation.Mesh.GetAabb().End.Y < court.Position.Y+court.Mesh.GetAabb().End.Y-.02f,"red court and foundation have separate depth planes");
        float crown=terrain.GetChildren().OfType<MeshInstance3D>()
            .Where(n=>Math.Abs(n.Position.X-5.5f)<.01f&&Math.Abs(n.Position.Z-.5f)<.01f)
            .Max(n=>n.Position.Y+n.Mesh.GetAabb().End.Y);
        Check(Math.Abs(crown-terrain.AimObstacleHeight(5,0)*TempleStageView.HeightScale)<.01f,"visible rear wall crown and shared throw collision agree");
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1280, 800) })
        {
            await ResizeWindow(size); CheckFraming(BlastStages.TopRules, size);
            await Capture($"top-rules-overview-{size.X}x{size.Y}");
        }
        await MoveTo(5.5f, 1.5f, 80); await MoveTo(2.5f, 1.5f, 80);
        await MoveTo(2.5f, 6.5f, 80);
        Check(Player.GetProperty("y").GetSingle() == 0, "rear-left green stairs descend to the red court");
        await MoveTo(5.5f, 6.5f, 80);
        Check(Player.GetProperty("y").GetSingle() == 0, "player walks beneath the left bridge without snapping upward");
        await Capture("top-rules-lower-underpass");
        Input(0, new PlayerInput(0, 0, A: true, B: true)); await Frames(2);
        Input(0, new PlayerInput(0, 0, B: true, Aim: true)); await Frames(5);
        Check(_root.ThrowPreviews.Any(p => p.EndpointMarkerVisible && p.Y == 0), "lower-story aim preview includes the bridge ceiling");
        await Capture("top-rules-under-bridge-aim");
        Input(0, default); await Frames(2);
        await MoveTo(2.5f, 6.5f, 80);
        await WaitState(s => s.GetProperty("bombs").GetArrayLength() == 0, "lower-story throw completes its ordinary fuse", 150);
        Check(Player.GetProperty("y").GetSingle() == 0, "lower throw preserves the player's story");
        await MoveTo(16.5f, 6.5f, 180);
        Check(Player.GetProperty("y").GetSingle() == 0, "continuous red floor crosses beneath all three lanes");
        await MoveTo(16.5f, 1.5f, 80);
        Check(Player.GetProperty("y").GetSingle() == 2, "rear-right stairs regain the upper perimeter");
        await MoveTo(13.5f, 1.5f, 80); await MoveTo(13.5f, 8.5f, 100); await MoveTo(5.5f, 8.5f, 120);
        Check(Player.GetProperty("y").GetSingle() == 2, "three pale junctions form a continuous raised crosswalk");
        await Capture("top-rules-upper-crosswalk");
        await MoveTo(5.5f, 15.5f, 100); await MoveTo(2.5f, 15.5f, 80); await MoveTo(2.5f, 10.5f, 80);
        Check(Player.GetProperty("y").GetSingle() == 0, "front-left staircase reaches the court");
        await MoveTo(16.5f, 10.5f, 180); await MoveTo(16.5f, 15.5f, 80);
        Check(Player.GetProperty("y").GetSingle() == 2, "front-right staircase completes all four connections");
        await Capture("top-rules-front-stairs");
        var lighting = _root.Preferences.StageLighting;
        await SelectLocalLighting(StageTime.Night); await Capture("top-rules-night");
        await SelectLocalLighting(lighting);
        _root.Back(); await Frames(3);
        _root.StartLocal(new SessionSettings(Stage: BlastStages.TopRules, Bots: 7, SuddenDeath: false), 1);
        await WaitState(s => s.GetProperty("phase").GetInt32() == 1, "eight-player castle battle begins", 150);
        await Frames(360);
        Check(State.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("kind").GetString() == "blast"), "real CPU battle produces explosions in the castle");
        await Capture("top-rules-eight-player-battle");
        _root.Back(); await Frames(3);
        await TopRulesDroppedLoot();
        _root.StartLocal(new SessionSettings(Stage: BlastStages.TopRules, Bots: 0, RoundSeconds: 60, Ghosts: false), 2);
        await WaitState(s => s.GetProperty("phase").GetInt32() == 2, "short castle battle enters sudden death", 150);
        await WaitState(s => s.GetProperty("closedLeft").GetInt32() >= 3, "sudden-death side walls close inward", 150);
        Check(State.GetProperty("closedLeft").GetInt32() == State.GetProperty("closedRight").GetInt32(), "castle compression is symmetric");
        await Capture("top-rules-inward-walls");
        _root.Back(); await Frames(3);
        menu.Configure(new SessionSettings(), 1);
    }
    private async Task TopRulesDroppedLoot()
    {
        var rules=new AdvancedRules{DropPercent=100,ItemSeconds=60,DisabledItems=string.Join(',',AdvancedRules.ItemTypes.Where(t=>t!="red"))};
        _root.StartLocal(new SessionSettings(Stage:BlastStages.TopRules,Bots:0,SuddenDeath:false,Ghosts:false){Advanced=rules},3);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"castle loot journey enters ordinary three-player battle",150);
        var layout=BlastStages.Get(BlastStages.TopRules);
        var bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool Covered(int x,int z){int i=z*layout.Width+x;return (bits[i/8]&(1<<(i%8)))!=0;}
        int crateZ=new[]{7,9}.First(z=>Covered(3,z));
        await MoveTo(5.5f,1.5f,80);await MoveTo(2.5f,1.5f,80);await MoveTo(2.5f,6.5f,100);
        await MoveTo(4.5f,6.5f,80);await MoveTo(4.5f,crateZ+.5f,80);
        Input(0,new PlayerInput(-1,0));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
        await MoveTo(4.5f,6.5f,80);await MoveTo(8.5f,6.5f,80);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"crate opening finishes its real fuse and blast",150);
        Check(State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("type").GetString()=="red"),"destroyed castle masonry reveals real equipment");
        await MoveTo(4.5f,6.5f,80);await MoveTo(4.5f,crateZ+.5f,80);await MoveTo(3.5f,crateZ+.5f,80);
        Check(Player.GetProperty("bombType").GetString()=="red","ordinary collection equips the red bomb");
        await MoveTo(4.5f,crateZ+.5f,80);await MoveTo(4.5f,6.5f,80);await MoveTo(5.5f,6.5f,80);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
        await WaitState(s=>!Player.GetProperty("alive").GetBoolean(),"real blast drops the equipped item under a bridge",150);
        await Frames(4);
        var item=State.GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("type").GetString()=="red");
        Check(item.GetProperty("cx").GetInt32()==5&&item.GetProperty("cz").GetInt32()==6&&item.GetProperty("y").GetSingle()==0,"death-drop snapshot retains lower-floor support");
        var pickup=AuditDescendants(LightingArena).OfType<PowerupModel>().Single();
        Check(Math.Abs(pickup.Position.Y-.48f)<.01f,"actual pickup mesh uses accepted lower-floor height instead of the bridge top");
        await Capture("top-rules-real-loot-under-bridge");
        _root.Back();await Frames(3);
    }

}
