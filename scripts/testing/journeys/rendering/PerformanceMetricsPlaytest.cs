using Godot;
using BlastCircuit.App;
using BlastCircuit.UI.Hud;
using Bomber.Client.Sessions;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PerformanceMetrics()
    {
        CtlReleaseAll();await ControllerFocusReady();_root.Navigate("Modes");await Frames(3);
        var bar=AuditDescendants(_root).OfType<PerformanceBar>().Single();
        var hud=AuditDescendants(_root).OfType<BattleHud>().Single();
        bool original=_root.Preferences.PerformanceMetrics;var originalSize=GetWindow().Size;
        try
        {
            if(original)await KeyPress(Key.F3);
            int samples=bar.SampleCount;await Frames(20);
            Check(!bar.Visible&&!bar.IsProcessing()&&bar.SampleCount==samples,"disabled metrics perform no sampling");
            await Capture("metrics-off");
            await ActivateNamed("OpenSettings");await ActivateNamed("PerformanceMetrics");
            Check(bar.Visible&&_root.Preferences.PerformanceMetrics,"Settings enables the actual overlay");
            Check(PlayerPreferences.Load(_root.PreferencesPath).PerformanceMetrics,"enabled preference persists");
            Check(NamedButton("PerformanceMetrics").HasFocus(),"metrics leave focus on the toggle");
            await Frames(40);
            Check(bar.SampleCount>samples&&bar.Fps>0&&bar.FrameMilliseconds>0&&double.IsFinite(bar.ProcessMilliseconds),"live engine counters update while visible");
            Event("performance-metrics",new {bar.Fps,bar.FrameMilliseconds,bar.ProcessMilliseconds,bar.VideoMemoryBytes,bar.DrawCalls,cpuTemperatureAvailable=false});
            Check(bar.MouseFilter==Control.MouseFilterEnum.Ignore&&bar.FocusMode==Control.FocusModeEnum.None,"overlay passes pointer input and takes no focus");
            await Capture("metrics-settings");
            Check(NamedButton("SettingsBack").GetGlobalRect().End.Y<GetViewport().GetVisibleRect().End.Y-60,"Settings Back stays above the input footer");
            // Repeated key-down events must not toggle repeatedly while holding the shortcut.
            Godot.Input.ParseInputEvent(new InputEventKey {Keycode=Key.F3,Pressed=true,Echo=true});await Frames(2);
            Check(bar.Visible,"F3 key repeat does not toggle metrics");
            await KeyPress(Key.F3);
            Check(!bar.Visible&&!NamedButton("PerformanceMetrics").ButtonPressed&&!PlayerPreferences.Load(_root.PreferencesPath).PerformanceMetrics,"F3 hides, persists and synchronizes Settings");
            await KeyPress(Key.F3);await KeyPress(Key.Escape);
            foreach(var size in new[]{new Vector2I(1024,640),new Vector2I(1280,720),new Vector2I(1280,800)})
            {
                await ResizeWindow(size);await Frames(20);
                Check(bar.Size.X>=GetViewport().GetVisibleRect().Size.X-1&&bar.Size.Y==PerformanceBar.BarHeight,"metrics span the viewport at "+size);
                await Capture("metrics-menu-"+size.X+"x"+size.Y);
                await MouseClick("Settings");Check(_root.ScreenName=="Settings","pointer activation works below metrics at "+size);
                AuditCards("metrics-settings-"+size.X+"x"+size.Y);
                Check(NamedButton("SettingsBack").GetGlobalRect().End.Y<GetViewport().GetVisibleRect().End.Y-60,"Settings footer remains clear at "+size);
                await Capture("metrics-settings-"+size.X+"x"+size.Y);await KeyPress(Key.Escape);
            }
            _root.StartLocal(new SessionSettings(Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"metrics practice reaches fight",180);
            var session=_root.ActiveSession;
            Check(bar.IsVisibleInTree()&&hud.GetGlobalRect().Position.Y>=bar.GetGlobalRect().End.Y,"battle HUD starts below metrics");
            await ControllerFocusReady();CtlKey(Key.J,true);await Frames(2);CtlKey(Key.J,false);await Frames(4);
            Check(_root.LatestState!.Value.GetProperty("bombs").GetArrayLength()>0,"ordinary gameplay input places a bomb with metrics enabled");
            await Capture("metrics-match");
            await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause","pause remains accessible with metrics");
            int snapshots=_root.SnapshotCount;samples=bar.SampleCount;await Frames(25);
            Check(_root.SnapshotCount==snapshots&&bar.SampleCount>samples,"telemetry updates while local authority is paused");
            await ActivateNamed("PauseSettings");await ActivateNamed("PerformanceMetrics");
            Check(!bar.Visible&&hud.OffsetTop==0&&ReferenceEquals(session,_root.ActiveSession),"disabling during play restores HUD spacing and retains the session");
            await KeyPress(Key.Escape);await ActivateNamed("ResumeGame");await Frames(4);
            Check(_root.ScreenName=="Match"&&ReferenceEquals(session,_root.ActiveSession),"resume retains the same match");
            await Capture("metrics-match-off");
            // Compare pixels in the stable strip corner, beyond the arena's player tokens.
            using var off=GetViewport().GetTexture().GetImage();
            await KeyPress(Key.F3);await Frames(20);await Capture("metrics-match-on");
            using var on=GetViewport().GetTexture().GetImage();
            int changed=0;int height=Math.Max(1,(int)(PerformanceBar.BarHeight*on.GetHeight()/GetViewport().GetVisibleRect().Size.Y));
            for(int y=2;y<height-2;y++)for(int x=2;x<on.GetWidth()/4;x+=4)
            {
                var difference=on.GetPixel(x,y)-off.GetPixel(x,y);
                if(Math.Abs(difference.R)+Math.Abs(difference.G)+Math.Abs(difference.B)>.1f)changed++;
            }
            Check(changed>100,"toggling produces visible pixels in the rendered top bar");
            var invalid=_root.PreferencesPath+".invalid";var config=new ConfigFile();
            config.SetValue("video","performanceMetrics","invalid");Check(config.Save(invalid)==Error.Ok,"invalid preference fixture saves");
            try {Check(!PlayerPreferences.Load(invalid).PerformanceMetrics,"malformed preference safely defaults off");}
            finally {DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(invalid));}
        }
        finally
        {
            CtlReleaseAll();_root.Navigate("Modes");
            if(_root.Preferences.PerformanceMetrics!=original)await KeyPress(Key.F3);
            await ResizeWindow(originalSize);
        }
    }
}
