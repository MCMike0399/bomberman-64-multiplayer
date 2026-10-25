using Godot;
using Bomber.Client;
using BlastCircuit.UI.Menu;
using BlastCircuit.Arena;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task SetupMenus()
    {
        _root.Navigate("Setup");await Frames(8);
        var state=_root.FindChild("SetupSettings",true,false)??throw new InvalidOperationException("Setup state missing.");
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        // Earlier journeys intentionally leave another arena selected. Own this fixture's starting state.
        menu.Configure(new SessionSettings(),1);
        Check(!((Control)state).IsVisibleInTree(),"settings state is retained behind native arcade controls");
        Check(NamedButton("StageSelector").HasFocus(),"setup begins on the arena selector");
        async Task Adjust(string name,Key key)
        {
            var dial=NamedButton(name);Check(dial.IsVisibleInTree()&&!dial.Disabled,"arcade selector is usable: "+name);
            dial.GrabFocus();await Frames(1);await KeyPress(key);Check(dial.HasFocus(),"horizontal adjustment retains focus: "+name);
        }
        Check(menu.Settings.Stage=="sunken-temple","setup starts with the configured temple");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="rock-garden","right selects authority Rock Garden");await Capture("setup-rock-garden");
        var garden=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama");
        var footprint=new Rect2(-.4f,-.4f,garden.Width+.8f,garden.Depth+.8f).Grow(.01f);
        var scenery=AuditDescendants(garden).OfType<MeshInstance3D>().Where(m=>m.IsVisibleInTree()&&m.Mesh!=null).ToArray();
        var spills=scenery.Where(m=>
        {
            var bounds=(garden.GlobalTransform.AffineInverse()*m.GlobalTransform)*m.Mesh.GetAabb();
            return !footprint.Encloses(new Rect2(bounds.Position.X,bounds.Position.Z,bounds.Size.X,bounds.Size.Z));
        }).Select(m=>m.Name.ToString()).ToArray();
        Event("garden-miniature-bounds",new {meshes=scenery.Length,spills});
        Check(scenery.Length>0&&spills.Length==0,"Rock Garden miniature scenery stays within its display footprint");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="up-and-down","right selects authority Up & Down");await Capture("setup-up-and-down");
        var upAndDown=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama");
        var displayMeshes=AuditDescendants(upAndDown).OfType<MeshInstance3D>().Where(m=>m.IsVisibleInTree()&&m.Mesh!=null).ToArray();
        var belowStand=displayMeshes.Where(m=>((upAndDown.GetParent<Node3D>().GlobalTransform.AffineInverse()*m.GlobalTransform)*m.Mesh.GetAabb()).Position.Y<-.312f).ToArray();
        Check(belowStand.Length==0,"Up & Down miniature foundations stop above the display stand");
        Check(displayMeshes.Any(m=>m.Mesh is CylinderMesh {RadialSegments:96} sea&&Mathf.IsEqualApprox(sea.TopRadius*upAndDown.Scale.X,4.45f)),
            "Up & Down miniature sea fits the circular stand without square water corners");
        if(!_headless)
        {
            using var image=Image.LoadFromFile(_screenshots[^1]);int green=0;
            for(int y=(int)(image.GetHeight()*.15);y<image.GetHeight()*.65;y++)
                for(int x=(int)(image.GetWidth()*.05);x<image.GetWidth()*.49;x++)
                {
                    var color=image.GetPixel(x,y);
                    if(color.G>color.R*.97f&&color.B<color.G*.77f&&color.G>.5f)green++;
                }
            Event("miniature-summit-visibility",new {green});
            Check(green>=40,"Up & Down green summit remains visible in the rendered setup miniature");
        }
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="top-rules","right selects authority Top Rules");await Capture("setup-top-rules");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="blizzard-battle","right selects Blizzard Battle");await Capture("setup-blizzard-battle");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="greedy-trap","right selects Greedy Trap");await Capture("setup-greedy-trap");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="sunken-temple","arena selection wraps to temple");
        await Adjust("StageSelector",Key.Left);Check(menu.Settings.Stage=="greedy-trap","left wraps to previous arena");await Adjust("StageSelector",Key.Right);
        await Adjust("Winstovictory",Key.Right);Check(menu.Settings.Wins==3,"wins dial changes settings");await Adjust("Winstovictory",Key.Left);Check(menu.Settings.Wins==2,"wins dial reverses");
        await Adjust("Roundtime",Key.Right);Check(menu.Settings.RoundSeconds==150,"time dial advances thirty seconds");await Adjust("Roundtime",Key.Left);Check(menu.Settings.RoundSeconds==120,"time dial reverses");
        await Adjust("CPUlevel",Key.Right);Check(menu.Settings.BotDifficulty=="hard","CPU dial changes difficulty");await Adjust("CPUlevel",Key.Left);Check(menu.Settings.BotDifficulty=="normal","CPU dial reverses");
        await Adjust("Suddendeath",Key.Right);Check(!menu.Settings.SuddenDeath,"sudden death dial disables rule");await Adjust("Suddendeath",Key.Left);Check(menu.Settings.SuddenDeath,"sudden death dial restores rule");
        await Adjust("Ghostcomeback",Key.Right);Check(!menu.Settings.Ghosts,"ghost dial disables comeback");await Adjust("Ghostcomeback",Key.Left);Check(menu.Settings.Ghosts,"ghost dial restores comeback");
        Check(!NamedButton("CPUrivals").IsVisibleInTree(),"local CPU count comes from player roster");
        NamedButton("Winstovictory").GrabFocus();await Frames(1);await KeyPress(Key.Down);
        Check(NamedButton("Roundtime").HasFocus(),"down navigates from win target to timer");await KeyPress(Key.Up);
        Check(NamedButton("Winstovictory").HasFocus(),"up returns from timer to win target");
        await Capture("setup-local-native");
        _root.Navigate("HostSetup");await Frames(5);
        Check(NamedButton("LaunchMatch").Text=="Save Room Settings","host launch has save action");
        await Adjust("CPUrivals",Key.Right);Check(menu.OnlineSettings.Bots==4,"host CPU rail reaches room settings");await Adjust("CPUrivals",Key.Left);Check(menu.OnlineSettings.Bots==3,"host CPU rail reverses");
        await Adjust("StageSelector",Key.Right);await Adjust("Winstovictory",Key.Right);await Adjust("Roundtime",Key.Right);await Adjust("CPUlevel",Key.Right);await Adjust("Suddendeath",Key.Right);await Adjust("Ghostcomeback",Key.Right);
        var saved=menu.OnlineSettings;
        Check(saved==new SessionSettings("rock-garden",3,3,150,false,false,"hard"),"all arcade controls produce exact private-room settings");
        await Capture("setup-host-native");await ActivateNamed("LaunchMatch");Check(_root.ScreenName=="Online","save room settings returns to online menu");
        _root.Navigate("HostSetup");await Frames(3);Check(menu.OnlineSettings==saved,"saved private room settings persist when reopened");
        _root.Navigate("Practice");await Frames(4);
        Check(NamedButton("LaunchMatch").Text=="Start Practice","practice launch has correct action");
        foreach(var name in new[]{"Winstovictory","Roundtime","CPUlevel","Suddendeath","Ghostcomeback"})Check(NamedButton(name).Disabled,"practice disables irrelevant rule: "+name);
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="up-and-down","practice can choose another authority arena");
        await Capture("setup-practice-native");await ActivateNamed("LaunchMatch");
        await WaitState(_=>_root.LatestStage?.GetProperty("id").GetString()=="up-and-down","practice launch uses selected arena",120);
        Check(State.GetProperty("players").GetArrayLength()==1,"practice starts with one contender");
        _root.Back();await Frames(3);
        menu.Configure(new SessionSettings(),1);_root.Navigate("Setup");await Frames(3);
        Check(NamedButton("LaunchMatch").Text=="Start Battle"&&!NamedButton("Winstovictory").Disabled,"local setup restores launch and editable rules after practice");
        // Rebuilding gameplay after the menu also catches accidental edits to cached shared meshes.
        await Adjust("StageSelector",Key.Right);_root.Navigate("Practice");await Frames(3);await ActivateNamed("LaunchMatch");
        await WaitState(_=>_root.LatestStage?.GetProperty("id").GetString()=="rock-garden","practice opens Rock Garden after miniature selection",120);
        var exterior=_root.FindChildren("GardenExterior","",true,false).OfType<MeshInstance3D>().Single(m=>m.GetViewport()==_root.GetViewport());
        Check(exterior.Mesh.GetAabb().Size==new Vector3(160,.28f,160),"gameplay keeps its full exterior grass after menu preview resizing");
        await Capture("setup-rock-garden-gameplay");_root.Back();await Frames(3);
        menu.Configure(new SessionSettings(),1);
        _root.Navigate("Modes");await Frames(3);
    }
}
