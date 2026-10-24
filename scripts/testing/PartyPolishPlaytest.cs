using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.App;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PartyPolish()=>await WithControllerFocusRetries(PartyPolishAttempt,"party-polish");
    private async Task PartyPolishAttempt()
    {
        await ControllerFocusReady();
        _root.Navigate("Online");await ControllerFrames(5);
        var menu=_root.FindChildren("*","",true,false).OfType<ArcadeMenu>().First();
        var name=(LineEdit)_root.FindChild("PlayerName",true,false)!;
        var server=(LineEdit)_root.FindChild("ServerUrl",true,false)!;
        Check(name.PlaceholderText=="Type your name"&&VisibleText(_root).Contains("click to edit"),"online identity explicitly invites editing");
        async Task TypeInto(LineEdit field,string text)
        {
            field.Text="";field.GrabFocus();await ControllerFrames(2);
            foreach(char c in text)
            {
                var key=(Key)char.ToUpperInvariant(c);
                Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Unicode=c,Pressed=true});await ControllerFrames(1);
                Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});await ControllerFrames(1);
            }
        }
        await TypeInto(name,"mika");await TypeInto(server,"http://127.0.0.1:18555");
        var saved=PlayerPreferences.Load(_root.PreferencesPath);
        Check(saved.PlayerName=="mika"&&saved.ServerUrl==server.Text,"typed name and server are persisted before exit");
        menu.ConfigurePreferences(saved);Check(name.Text=="mika"&&server.Text==saved.ServerUrl,"startup preference path restores identity and server");
        await Capture("online-editable-identity");
        _root.Navigate("Setup");await ControllerFrames(3);
        Check(menu.Settings.Wins==2&&menu.Settings.RoundSeconds==120&&menu.Settings.SuddenDeath,"default rules are first to two with one minute normal and one minute sudden death");
        await Capture("default-match-rules");
        await ControllerFocusReady();
        // Native key -> device hub -> local session -> authority stop, followed by the real fuse.
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"stop trial reaches fight",160);await ControllerFrames(5);
        Check(TraversalModel(0).DisplayName=="P1","unnamed player renders seat fallback above character");
        CtlKey(Key.J,true);await ControllerFrames(2);CtlKey(Key.J,false);await ControllerFrames(2);
        CtlKey(Key.J,true);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()>0&&s.GetProperty("bombs")[0].GetProperty("state").GetInt32()==1,"native second bomb press kicks",30);
        CtlKey(Key.J,false);CtlKey(Key.O,true);CtlKey(Key.O,false);await ControllerFrames(4);
        var stopped=State.GetProperty("bombs")[0];
        Check(stopped.GetProperty("state").GetInt32()==0,"sub-frame O stops the kicked bomb through production input");
        float z=stopped.GetProperty("z").GetSingle();await ControllerFrames(5);
        Check(State.GetProperty("bombs")[0].GetProperty("z").GetSingle()==z,"stopped bomb stays in place");
        await Capture("keyboard-stops-kick");
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0,"stopped bomb keeps ordinary fuse",100);
        _root.Back();await ControllerFrames(3);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"diagonal ramp witness reaches fight",160);
        await GrappleWalk(0,6,12);await GrappleWalk(0,6,13);
        Input(0,new PlayerInput(1,-1));await ControllerFrames(1);Input(0,new PlayerInput(1,1));await ControllerFrames(2);
        Input(0,new PlayerInput(0,0,A:true));await ControllerFrames(2);Input(0,default);await ControllerFrames(2);
        Input(0,new PlayerInput(1,1,A:true));
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()>0&&s.GetProperty("bombs")[0].GetProperty("state").GetInt32()==1,"diagonal kick leaves upper temple walkway",25);
        Input(0,default);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()>0&&s.GetProperty("bombs")[0].GetProperty("z").GetSingle()>15.5f&&s.GetProperty("bombs")[0].GetProperty("y").GetSingle()<.1f,"diagonal bomb crosses stair corner and rolls into lower courtyard",45);
        await Capture("pyramid-diagonal-ramp-descent");_root.Back();await ControllerFrames(3);
        // A normal three-human battle retains a live ghost after actual water contact.
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0,SuddenDeath:false),3);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"water return battle reaches fight",160);
        await GrappleWalk(0,12,13);await GrappleWalk(0,5,13);Input(0,new PlayerInput(-1,0));
        await WaitState(s=>s.GetProperty("players")[0].GetProperty("ghost").GetBoolean(),"water fall produces ghost comeback",130);
        Input(0,default);await ControllerFrames(6);
        Check(Player.GetProperty("alive").GetBoolean()&&Player.GetProperty("y").GetSingle()>=1,"ghost returns alive on dry temple ground");
        var model=TraversalModel(0);Check(model.Position.Y>0,"returned ghost is rendered above land");
        await Capture("water-ghost-return");
        float before=Player.GetProperty("z").GetSingle();Input(0,new PlayerInput(0,1));await ControllerFrames(6);Input(0,default);
        Check(Player.GetProperty("z").GetSingle()>before+.2f,"returned ghost can move through ordinary controls");
        _root.Back();CtlReleaseAll();await ControllerFrames(3);
    }
}
