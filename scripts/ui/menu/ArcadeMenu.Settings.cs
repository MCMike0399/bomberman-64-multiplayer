using Godot;
using Bomber.Client.Startup;
using BlastCircuit.App;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI.Menu;

/// <summary>Settings and pause screens, local preferences and update status.</summary>
public partial class ArcadeMenu
{
    private void BuildSettings()
    {
        var screen=Screen("Settings");var box=Card(screen,new(150,65),new(1140,740));
        ArcadeTheme.Heading(box,"Settings",42);
        Copy(box,"Your preferences are saved on this device.",18);
        var scroll=new ScrollContainer {Name="SettingsScroll",SizeFlagsVertical=SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};box.AddChild(scroll);
        var columns=ArcadeTheme.Row(scroll,40);columns.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var player=ArcadeTheme.Column(columns,14);player.CustomMinimumSize=new(500,440);
        var updates=ArcadeTheme.Column(columns,6);updates.CustomMinimumSize=new(500,440);
        _volumeLabel=ArcadeTheme.Text(player,"Sound volume: 80%",22);
        _volume=new HSlider {Name="SoundVolume",MinValue=0,MaxValue=100,Step=5,Value=80,CustomMinimumSize=new(460,48)};player.AddChild(_volume);
        _volume.ValueChanged+=value=>{_volumeLabel.Text=$"Sound volume: {value:0}%";SettingsChanged?.Invoke();};
        _vibration=new ArcadeToggle {Name="Vibration",Text="Controller vibration",CustomMinimumSize=new(460,56)};player.AddChild(_vibration);
        _vibration.Toggled+=_=>SettingsChanged?.Invoke();
        Copy(player,"Vibration is off by default. Enable it for bomb and impact feedback on supported controllers.",18);
        ArcadeTheme.Button(player,"Test vibration",()=>TestVibrationRequested?.Invoke()).Name="TestVibration";
        _settingsNotice=Copy(player,"",16);
        var lighting=new ArcadeRuleDial {Name="StageLighting",Caption="Local lighting",ValueWidth=160,
            ReadValue=()=>StageLighting.For(LocalStageTime).Label,
            ChangeValue=direction=>{LocalStageTime=(StageTime)(((int)LocalStageTime+direction+4)%4);SettingsChanged?.Invoke();}};
        player.AddChild(lighting);
        Copy(player,"Morning, midday, afternoon or night. Online rounds use the server's lighting.",16);
        _performanceMetrics=new ArcadeToggle {Name="PerformanceMetrics",Text="Performance metrics · F3",CustomMinimumSize=new(460,56),
            TooltipText="FPS, frame timing, video memory and draw calls. CPU temperature is unavailable: Godot has no portable sensor API."};player.AddChild(_performanceMetrics);
        _performanceMetrics.Toggled+=_=>SettingsChanged?.Invoke();
        var graphics=new ArcadeRuleDial {Name="GraphicsPreset",Caption="Graphics",ValueWidth=160,
            ReadValue=()=>Graphics.ToString(),ChangeValue=direction=>
            {Graphics=(GraphicsPreset)(((int)Graphics+direction+3)%3);_graphicsDescription.Text=GraphicsQuality.Description(Graphics);SettingsChanged?.Invoke();}};
        updates.AddChild(graphics);
        _graphicsDescription=Copy(updates,GraphicsQuality.Description(Graphics),16);
        if(OperatingSystem.IsWindows())
        {
            string folder=WindowsLaunchGuard.Argument(OS.GetCmdlineUserArgs(),"--launcher-state-dir")??Path.Combine(OS.GetUserDataDir(),"startup");
            var launch=new WindowsLaunchGuard(folder);string[] choices=["auto","vulkan","d3d12"],labels=["Auto","Vulkan","DirectX 12"];
            string selected=launch.ReadPreference().Renderer;
            var rendererHint=Copy(updates,"Renderer changes apply the next time you open the game.",16);
            var renderer=new ArcadeRuleDial {Name="RendererChoice",Caption="Renderer",ValueWidth=190,
                ReadValue=()=>labels[Array.IndexOf(choices,selected)],ChangeValue=direction=>
                {
                    string next=choices[(Array.IndexOf(choices,selected)+direction+choices.Length)%choices.Length];
                    try {launch.SelectRenderer(next);selected=next;rendererHint.Text="Saved. Reopen the game to use this renderer.";}
                    catch(IOException){rendererHint.Text="Could not save the renderer choice. Check this folder's write permissions.";}
                    catch(UnauthorizedAccessException){rendererHint.Text="Could not save the renderer choice. Check this folder's write permissions.";}
                }};
            updates.AddChild(renderer);
        }
        var mode=new ArcadeRuleDial {Name="DisplayMode",Caption="Display mode",ValueWidth=160,
            ReadValue=()=>Fullscreen?"Fullscreen":"Windowed",
            ChangeValue=_=>{Fullscreen=!Fullscreen;SettingsChanged?.Invoke();}};
        updates.AddChild(mode);
        var resolution=new ArcadeRuleDial {Name="DisplayResolution",Caption="Resolution",ValueWidth=190,
            ReadValue=()=>DisplayModeController.ResolutionLabel(Resolution,DisplayModeController.Current.ScreenPixels),ChangeValue=direction=>
            {
                var sizes=DisplayModeController.Current.AvailableResolutions();
                Resolution=sizes[(Array.IndexOf(sizes,Resolution)+direction+sizes.Length)%sizes.Length];SettingsChanged?.Invoke();
            }};
        updates.AddChild(resolution);
        _resolutionHint=Hint(updates,ResolutionHint(),16);
        DisplayModeController.Current.RenderSizeChanged+=()=>{if(IsInstanceValid(_resolutionHint))_resolutionHint.Text=ResolutionHint();};
        _frameCapLabel=ArcadeTheme.Text(updates,FrameCapText(0),22);
        _frameCap=new HSlider {Name="FrameCap",MinValue=0,MaxValue=PlayerPreferences.FrameCaps.Length-1,Step=1,
            Value=PlayerPreferences.FrameCaps.Length-1,CustomMinimumSize=new(460,40),
            TooltipText="Limits rendered frames to save power and heat. Unlimited still follows display sync."};updates.AddChild(_frameCap);
        _frameCap.ValueChanged+=_=>{_frameCapLabel.Text=FrameCapText(FrameCap);SettingsChanged?.Invoke();};
        Section(updates,"Game updates");
        _automaticUpdates=new ArcadeToggle {Name="AutomaticUpdates",Text="Check automatically",CustomMinimumSize=new(0,56),ButtonPressed=true};updates.AddChild(_automaticUpdates);
        _automaticUpdates.Toggled+=_=>SettingsChanged?.Invoke();
        _updateStatus=Copy(updates,"",17);

        var updateActions=ArcadeTheme.Row(updates,10);
        _checkUpdates=ArcadeTheme.Button(updateActions,"Check now",()=>CheckUpdatesRequested?.Invoke());_checkUpdates.Name="CheckUpdates";
        _installUpdate=ArcadeTheme.Button(updateActions,"Restart to update",()=>InstallUpdateRequested?.Invoke(),true);_installUpdate.Name="InstallUpdate";_installUpdate.Visible=false;
        var back=ArcadeTheme.Button(box,"Back",()=>BackRequested?.Invoke());back.Name="SettingsBack";
        RegisterNavigation("Settings",[],[back],Choices(player),Choices(updates));
    }

    private void BuildPause()
    {
        var screen=Screen("Pause");var box=Card(screen,new(420,150),new(600,520));
        ArcadeTheme.Heading(box,"Game menu",42);
        _pauseHint=Copy(box,"",18);
        ArcadeTheme.Button(box,"Resume game",()=>ResumeRequested?.Invoke(),true).Name="ResumeGame";
        _retryLesson=ArcadeTheme.Button(box,"Retry lesson",()=>{if(_pausedTutorial is {} id)TutorialRequested?.Invoke(id);});_retryLesson.Name="PauseRetryLesson";_retryLesson.Visible=false;
        ArcadeTheme.Button(box,"Settings",()=>Navigation?.Invoke("Settings")).Name="PauseSettings";
        _pauseLeave=ArcadeTheme.Button(box,"Leave game",()=>Navigation?.Invoke(_pausedTutorial is null?"Modes":"Tutorial"));_pauseLeave.Name="LeaveGame";
        RegisterNavigation("Pause",[],[],Choices(box));
    }

    public void ConfigurePreferences(PlayerPreferences preferences)
    {
        _name.Text=preferences.PlayerName;_server.Text=preferences.ServerUrl;
        _automaticUpdates.SetPressedNoSignal(preferences.AutomaticUpdates);
        _vibration.SetPressedNoSignal(preferences.Vibration);_volume.SetValueNoSignal(preferences.Volume*100);
        _volumeLabel.Text=$"Sound volume: {preferences.Volume*100:0}%";
        LocalStageTime=preferences.StageLighting;
        ConfigurePerformanceMetrics(preferences.PerformanceMetrics);
        _frameCap.SetValueNoSignal(Array.IndexOf(PlayerPreferences.FrameCaps,PlayerPreferences.ValidFrameCap(preferences.FrameCap)));
        _frameCapLabel.Text=FrameCapText(FrameCap);
        ConfigureDisplay(preferences.Fullscreen,preferences.Resolution);
        Graphics=preferences.Graphics;_graphicsDescription.Text=GraphicsQuality.Description(Graphics);
    }

    /// <summary>Resolution applies immediately; state the real 3D output so every choice is observable.</summary>
    private static string ResolutionHint()
    {
        var render=DisplayModeController.Current.RenderSize;
        if(render==Vector2I.Zero)return "Window size, or fullscreen 3D resolution. Fits your display; menus stay sharp.";
        return $"3D renders at {render.X} x {render.Y}. Applies instantly; menus stay sharp.";
    }

    private static string FrameCapText(int cap)=>cap==0?"Frame rate cap: Unlimited":$"Frame rate cap: {cap} FPS";

    public bool Fullscreen {get;private set;}=true;
    public void ConfigurePerformanceMetrics(bool enabled)=>_performanceMetrics.SetPressedNoSignal(enabled);
    public Vector2I Resolution {get;private set;}
    public void ConfigureDisplay(bool fullscreen,Vector2I resolution){Fullscreen=fullscreen;Resolution=resolution;}

    public void UpdateStatus(string version,string status,bool busy,bool ready,bool canInstall)
    {
        _updateStatus.Text=$"{version} · {status}";
        _settingsButton.Text=ready?"Settings · Update ready":"Settings";
        bool changed=_checkUpdates.Disabled!=(busy||ready)||_installUpdate.Visible!=ready||_installUpdate.Disabled!=(busy||!canInstall);
        _checkUpdates.Disabled=busy||ready;_installUpdate.Visible=ready;_installUpdate.Disabled=busy||!canInstall;
        if(changed&&_screen=="Settings")
        {
            RefreshNavigation();
            if(!CanFocus(GetViewport().GuiGetFocusOwner()))FocusScreen(restore:true);
        }
        _installUpdate.TooltipText=canInstall?"Restart to install the verified update.":"Leave your current room before updating.";
    }

    public void SettingsNotice(string text)=>_settingsNotice.Text=text;

    public void ShowPause(bool online,bool training=false,string? lesson=null,bool restoreFocus=false)
    {
        _pausedTutorial=training?lesson??"free":null;_retryLesson.Visible=training;
        _pauseLeave.Text=training?"Back to lessons":"Leave game";
        _pauseHint.Text=online?"The online match continues while this menu is open.":training?"Your lesson is paused. Retry at any time.":"The local game is paused.";ShowScreen("Pause",restoreFocus);
    }
}
