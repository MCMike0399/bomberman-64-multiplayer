using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.App;
using BlastCircuit.InputDevices;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PlayerSettingsMenu()
    {
        var hub=ControllerHub.Current;
        _root.Navigate("Modes");await Frames(3);await ActivateNamed("OpenSettings");
        Check(_root.ScreenName=="Settings","main menu opens dedicated settings");
        Check(!_root.Preferences.Vibration&&!hub.VibrationEnabled,"vibration defaults off");
        var pad=QaPads[0];hub.OverrideDevicesForPlaytest([pad]);
        try
        {
            int count=hub.VibrationRequestCount;await ActivateNamed("TestVibration");
            Check(hub.VibrationRequestCount==count,"disabled vibration sends no motor request");
            await ActivateNamed("Vibration");Check(_root.Preferences.Vibration,"player can enable vibration");
            await ActivateNamed("TestVibration");Check(hub.VibrationRequestCount==count+1,"enabled test routes to controller backend");
            var saved=PlayerPreferences.Load(_root.PreferencesPath);Check(saved.Vibration,"vibration preference persists to disk");
            var slider=_root.FindChild("SoundVolume",true,false) as HSlider??throw new Exception("Missing volume slider");
            slider.GrabFocus();await KeyPress(Key.Left);
            Check(_root.Preferences.Volume<.8f&&_root.BattleAudio.Volume==_root.Preferences.Volume,"volume control changes actual game audio");
            Check(PlayerPreferences.Load(_root.PreferencesPath).Volume==_root.Preferences.Volume,"sound preference persists");
            Check(_root.Preferences.AutomaticUpdates,"automatic update checking defaults on");
            await ActivateNamed("AutomaticUpdates");Check(!PlayerPreferences.Load(_root.PreferencesPath).AutomaticUpdates,"automatic update preference persists");
            await ActivateNamed("AutomaticUpdates");
            await Capture("player-settings");await KeyPress(Key.Escape);Check(_root.ScreenName=="Modes","Esc returns from settings");
            _root.StartLocal(new SessionSettings(Bots:0),1,practice:true);await Frames(5);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"haptic gameplay reaches fight",180);
            count=hub.VibrationRequestCount;CtlKey(Key.J,true);await Frames(2);CtlKey(Key.J,false);await Frames(5);
            Check(hub.VibrationRequestCount==count+1,"accepted bomb placement sends one haptic pulse");
            await Frames(8);Check(hub.VibrationRequestCount==count+1,"repeated snapshots do not repeat haptics");
            var session=_root.ActiveSession;await KeyPress(Key.Escape);
            Check(_root.ScreenName=="Pause"&&ReferenceEquals(session,_root.ActiveSession),"Esc opens game menu without leaving session");
            int snapshots=_root.SnapshotCount;await Frames(15);
            Check(_root.SnapshotCount==snapshots,"local authority pauses in game menu");
            await ActivateNamed("PauseSettings");Check(_root.ScreenName=="Settings"&&ReferenceEquals(session,_root.ActiveSession),"in-game settings retain session");
            await ActivateNamed("Vibration");Check(!hub.VibrationEnabled,"vibration can be turned off during a game");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause","settings back restores game menu");
            await Capture("game-menu");
            CtlButton(pad.RuntimeId,JoyButton.Start,true);await Frames(1);CtlButton(pad.RuntimeId,JoyButton.Start,false);await Frames(2);
            Check(NamedButton("ResumeGame").HasFocus(),"Start focuses the primary menu action");
            await ActivateNamed("ResumeGame");Check(_root.ScreenName=="Match"&&ReferenceEquals(session,_root.ActiveSession),"resume keeps same match");
            await Frames(8);Check(_root.SnapshotCount>snapshots,"local authority resumes");
            CtlButton(pad.RuntimeId,JoyButton.Start,true);await Frames(1);CtlButton(pad.RuntimeId,JoyButton.Start,false);await Frames(2);
            Check(_root.ScreenName=="Pause","controller Start opens game menu");
            await ActivateNamed("LeaveGame");Check(_root.ScreenName=="Modes"&&_root.ActiveSession==null,"only explicit Leave game exits");
        }
        finally {CtlReleaseAll();hub.RestorePhysicalDevicesForPlaytest();_root.Navigate("Modes");}
    }
}
