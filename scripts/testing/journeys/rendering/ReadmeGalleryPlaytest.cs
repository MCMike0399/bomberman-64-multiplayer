using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

/// <summary>Reproducible documentation captures from real sessions at the highest preset.</summary>
public partial class PlaytestDriver
{
    private async Task ReadmeGallery()
    {
        Check(!_headless,"README gallery requires real rendered pixels");
        Check(DisplayServer.GetName()=="macOS","README gallery requires the native Mac desktop");
        // Cocoa constrains decorated windows to the desktop height. A borderless
        // capture window can render the complete 4K target beyond the display.
        GetWindow().Borderless=true;
        await ResizeWindow(new Vector2I(3840,2400));
        _root.Navigate("Modes");await Frames(6);
        await ControllerFocusReady();
        await SelectGraphics(GraphicsPreset.High);
        CheckMenuSampling();
        await Capture("gallery-mode-select");
        await ActivateNamed("OpenSettings");await Frames(6);
        Check(_root.Preferences.Graphics==GraphicsPreset.High,"gallery settings show High");
        await Capture("gallery-settings");await KeyPress(Key.Escape);
        foreach(string screen in new[]{"Roster","Setup","Online","Controls","Tutorial"})
        {
            _root.Navigate(screen);await Frames(30);
            Check(_root.ScreenName==screen,"gallery opens "+screen);
            await Capture("gallery-"+screen.ToLowerInvariant());
        }
        await ActivateNamed("Lesson_move");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"gallery enters movement lesson",180);
        await Capture("gallery-tutorial-lesson");
        _root.Navigate("Modes");await Frames(4);
        foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap","circuit-garden"})
        {
            bool practice=stage=="circuit-garden";
            _root.StartLocal(new SessionSettings(Stage:stage,Bots:practice?0:7),1,practice:practice);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"gallery enters "+stage,180);
            await Frames(6);
            var view=_root.GetViewport();
            Check(_root.Preferences.Graphics==GraphicsPreset.High&&view.Msaa3D==Viewport.Msaa.Msaa4X&&Mathf.IsEqualApprox(view.Scaling3DScale,1),"gallery uses native resolution and High 4x AA: "+stage);
            Check(GetWindow().Size==new Vector2I(3840,2400),"gallery output is 3840x2400: "+stage);
            await Capture("gallery-"+stage);
            if(!practice){await Frames(90);await Capture("gallery-"+stage+"-action");}
            Input(0,default);_root.Navigate("Modes");await Frames(4);
        }
        // Existing accepted-input journey captures the current small, charged and power blasts.
        await PresentationEffects();
        await Results();
        Check(_root.Preferences.Graphics==GraphicsPreset.High,"all gallery effects retain High");
        Event("readme-gallery",new {preset="high",width=3840,height=2400,lighting=_root.Preferences.StageLighting.ToString(),camera="ordinary battle camera",simulation="real local sessions, seed 64"});
    }
}
