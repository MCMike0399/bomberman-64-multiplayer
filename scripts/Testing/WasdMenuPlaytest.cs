using Godot;
using BlastCircuit.UI;
using BlastCircuit.InputDevices;
using Bomber.Client;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private Button VisibleButton(string text)=>_root.FindChildren("*","",true,false).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text.Contains(text,StringComparison.Ordinal));
    private async Task WasdMenus()
    {
        CtlReleaseAll();await ControllerFocusReady();_root.Navigate("Modes");await Frames(3);
        var local=VisibleButton("LOCAL BATTLE");var online=VisibleButton("ONLINE BATTLE");
        local.GrabFocus();await KeyPress(Key.S);
        Check(online.HasFocus(),"S moves native main-menu focus down");await KeyPress(Key.W);
        Check(local.HasFocus(),"W moves native main-menu focus up");
        await KeyPress(Key.Down);Check(online.HasFocus(),"arrow navigation remains available alongside WASD");
        await Capture("wasd-main-menu");
        _root.Navigate("Setup");await Frames(4);
        var state=_root.FindChild("SetupSettings",true,false)!;
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        menu.Configure(new SessionSettings(),1);
        NamedButton("StageSelector").GrabFocus();await KeyPress(Key.D);
        Check(menu.Settings.Stage=="rock-garden","D changes the native arena selector");await KeyPress(Key.A);
        Check(menu.Settings.Stage=="sunken-temple","A reverses the native arena selector");
        NamedButton("Winstovictory").GrabFocus();await KeyPress(Key.S);
        Check(NamedButton("Roundtime").HasFocus(),"S moves between actual setup rules");await KeyPress(Key.W);
        Check(NamedButton("Winstovictory").HasFocus(),"W returns to the prior setup rule");
        NamedButton("CPUlevel").GrabFocus();await KeyPress(Key.A);
        Check(menu.Settings.BotDifficulty=="easy","A selects Easy CPU difficulty");await KeyPress(Key.D);
        Check(menu.Settings.BotDifficulty is "normal" or "medium"&&VisibleText(_root).Contains("MEDIUM"),"D displays Medium CPU difficulty");
        await KeyPress(Key.D);Check(menu.Settings.BotDifficulty=="hard","D selects Hard CPU difficulty");await KeyPress(Key.A);
        await Capture("wasd-difficulty-selector");
        _root.Navigate("Roster");await Frames(4);
        var role=NamedButton("Slot0Role") as OptionButton??throw new InvalidOperationException("Slot0Role missing");
        role.GrabFocus();await KeyPress(Key.Enter);var popup=role.GetPopup();
        Check(popup.Visible,"actual roster popup opens for WASD input");
        async Task Popup(Key key)
        {
            Godot.Input.ParseInputEvent(new InputEventKey {WindowId=popup.GetWindowId(),PhysicalKeycode=key,Keycode=key,Pressed=true});await Frames(1);
            Godot.Input.ParseInputEvent(new InputEventKey {WindowId=popup.GetWindowId(),PhysicalKeycode=key,Keycode=key,Pressed=false});await Frames(2);
        }
        // The OS pointer can initially hover the final item after earlier menu
        // tests. Establish a navigable endpoint through real keys before testing S.
        for(int i=0;i<popup.ItemCount&&popup.GetFocusedItem()>0;i++)await Popup(Key.W);
        Check(popup.GetFocusedItem()==0,"W reaches the first roster popup entry");
        await Popup(Key.S);Check(popup.GetFocusedItem()==1,"S navigates the actual roster popup");
        await Popup(Key.W);Check(popup.GetFocusedItem()==0,"W reverses popup navigation");await Popup(Key.Escape);
        _root.Navigate("Online");await Frames(4);
        var field=_root.FindChildren("*","",true,false).OfType<LineEdit>().First(e=>e.IsVisibleInTree());
        string original=field.Text;field.Text="";field.GrabFocus();
        foreach(var (key,character) in new[]{(Key.W,'w'),(Key.A,'a'),(Key.S,'s'),(Key.D,'d')})
        {
            Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Unicode=character,Pressed=true});await Frames(1);
            Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});await Frames(1);
        }
        Check(field.HasFocus()&&field.Text=="wasd","WASD remains ordinary text in the online form");field.Text=original;
        menu.Configure(new SessionSettings(),1);_root.Navigate("Modes");CtlReleaseAll();await Frames(3);
    }
}
