using Godot;
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
        var columns=ArcadeTheme.Row(box,40);
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
        var mode=new ArcadeRuleDial {Name="DisplayMode",Caption="Display mode",ValueWidth=160,
            ReadValue=()=>Fullscreen?"Fullscreen":"Windowed",
            ChangeValue=_=>{Fullscreen=!Fullscreen;SettingsChanged?.Invoke();}};
        updates.AddChild(mode);
        var resolution=new ArcadeRuleDial {Name="DisplayResolution",Caption="Resolution",ValueWidth=190,
            ReadValue=()=>DisplayModeController.ResolutionLabel(Resolution),ChangeValue=direction=>
            {
                var sizes=DisplayModeController.Resolutions;
                Resolution=sizes[(Array.IndexOf(sizes,Resolution)+direction+sizes.Length)%sizes.Length];SettingsChanged?.Invoke();
            }};
        updates.AddChild(resolution);
        Hint(updates,"Window size, or fullscreen 3D resolution. Fits your display; menus stay sharp.",16);
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
        ConfigureDisplay(preferences.Fullscreen,preferences.Resolution);
        Graphics=preferences.Graphics;_graphicsDescription.Text=GraphicsQuality.Description(Graphics);
    }

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
