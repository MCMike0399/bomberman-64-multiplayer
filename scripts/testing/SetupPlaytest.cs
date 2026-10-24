using Godot;
using Bomber.Client;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task SetupMenus()
    {
        _root.Navigate("Setup");await Frames(8);
        var state=_root.FindChild("SetupSettings",true,false)??throw new InvalidOperationException("Setup state missing.");
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        Check(!((Control)state).IsVisibleInTree(),"settings state is retained behind native arcade controls");
        Check(NamedButton("StageSelector").HasFocus(),"setup begins on the arena selector");
        async Task Adjust(string name,Key key)
        {
            var dial=NamedButton(name);Check(dial.IsVisibleInTree()&&!dial.Disabled,"arcade selector is usable: "+name);
            dial.GrabFocus();await Frames(1);await KeyPress(key);Check(dial.HasFocus(),"horizontal adjustment retains focus: "+name);
        }
        Check(menu.Settings.Stage=="sunken-temple","setup starts with the configured temple");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="rock-garden","right selects authority Rock Garden");await Capture("setup-rock-garden");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="up-and-down","right selects authority Up & Down");await Capture("setup-up-and-down");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="top-rules","right selects authority Top Rules");await Capture("setup-top-rules");
        await Adjust("StageSelector",Key.Right);Check(menu.Settings.Stage=="sunken-temple","arena selection wraps to temple");
        await Adjust("StageSelector",Key.Left);Check(menu.Settings.Stage=="top-rules","left wraps to previous arena");await Adjust("StageSelector",Key.Right);
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
        _root.Navigate("Modes");await Frames(3);
    }
}
