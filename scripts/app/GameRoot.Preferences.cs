using Godot;
using Bomber.Client.Sessions;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Effects;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
namespace BlastCircuit.App;

/// <summary>Device-local preferences, graphics preparation and menu lighting; never authority state.</summary>
public partial class GameRoot
{
    private void ApplyPreferences()
    {
        _uiAudio.Volume=_preferences.Volume;_arena.Audio.Volume=_preferences.Volume;
        ControllerHub.Current.VibrationEnabled=_preferences.Vibration;
        if(_appliedGraphics!=_preferences.Graphics)
        {
            GraphicsQuality.Apply(_preferences.Graphics,GetViewport());
            _arena.ApplyGraphicsQuality();
            _appliedGraphics=_preferences.Graphics;
        }
        ApplyStageLighting();
    }

    private void ApplyStageLighting()
    {
        var time=_preferences.StageLighting;
        if(_session is NetworkSession)
            time=LatestState is {} state&&state.TryGetProperty("stageLighting",out var lighting)&&lighting.ValueKind==JsonValueKind.String
                ?StageLighting.FromWire(lighting.GetString()):StageTime.Midday;
        if(_arena.TimeOfDay!=time)_arena.SetTimeOfDay(time);
    }

    private CanvasLayer PreparationOverlay(string message)
    {
        var layer=new CanvasLayer {Layer=100};AddChild(layer);
        var backdrop=new ColorRect {Color=ArcadeTheme.Night,FocusMode=Control.FocusModeEnum.All};layer.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);backdrop.GrabFocus();
        var center=new CenterContainer();backdrop.AddChild(center);center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column=ArcadeTheme.Column(center,24);
        ArcadeTheme.Heading(column,"Blast Circuit",54).HorizontalAlignment=HorizontalAlignment.Center;
        ArcadeTheme.Text(column,message,24).HorizontalAlignment=HorizontalAlignment.Center;
        return layer;
    }

    private async void SavePreferences()
    {
        bool graphicsChanged=_preferences.Graphics!=_menu.Graphics;
        _preferences.PlayerName=_menu.PlayerName;_preferences.ServerUrl=_menu.ServerUrl;
        _preferences.AutomaticUpdates=_menu.AutomaticUpdates;
        _preferences.StageLighting=_menu.LocalStageTime;
        _preferences.Graphics=_menu.Graphics;
        _preferences.Vibration=_menu.VibrationEnabled;_preferences.Volume=_menu.SoundVolume;ApplyPreferences();
        if(_preferences.Save(PreferencesPath)!=Error.Ok)_menu.SettingsNotice("Could not save settings on this device.");
        if(graphicsChanged&&!_skipGraphicsPreparation)
        {
            string screen=ScreenName;bool menuVisible=_menu.Visible;var focus=GetViewport().GuiGetFocusOwner();
            _preparingGraphics=true;_menu.Visible=false;ControllerHub.Current.MenuNavigationEnabled=false;
            var loading=PreparationOverlay("Applying graphics...");
            try {await EffectWarmup.Draw(this);await _arena.PrepareEffects();}
            finally
            {
                loading.QueueFree();_preparingGraphics=false;
                if(ScreenName==screen)_menu.Visible=menuVisible;
                ControllerHub.Current.MenuNavigationEnabled=ScreenName is not ("Match" or "Controls");
                if(_menu.Visible)
                {
                    if(GodotObject.IsInstanceValid(focus)&&focus.IsVisibleInTree())focus.GrabFocus();
                    else _menu.FocusStart();
                }
            }
        }
    }
}
