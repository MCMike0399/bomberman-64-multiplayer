using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.Rendering;
using BlastCircuit.App;
using BlastCircuit.Arena;
using System.Text.Json;

namespace BlastCircuit.Testing;

/// <summary>Reproducible documentation captures from real sessions at the highest preset.</summary>
public partial class PlaytestDriver
{
    /// <summary>Comparable real-session captures for the user's final all-map review.
    /// This is a gallery journey, not a frame-time or pixel-equivalence claim.</summary>
    private Task StageReviewMatrix()=>StageReviewMatrixCore(false);
    private Task TrainingReviewMatrix()=>StageReviewMatrixCore(true);

    private async Task StageReviewMatrixCore(bool trainingOnly)
    {
        Check(!_headless,"stage review matrix requires actual rendered pixels");
        var originalPreset=_root.Preferences.Graphics;var originalLighting=_root.Preferences.StageLighting;
        var originalSize=GetWindow().Size;var rows=new List<object>();
        string[] stages=trainingOnly?["circuit-garden"]:["sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap",
            "field-of-grass","in-the-gutter","sea-sick","lost-at-sea","circuit-garden"];
        try
        {
            await ResizeWindow(new Vector2I(1280,800));await ControllerFocusReady();
            foreach(string stage in stages)
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            foreach(var lighting in new[]{StageTime.Midday,StageTime.Night})
            {
                _root.Navigate("Modes");await Frames(3);
                await SelectGraphics(preset);await SelectLocalLighting(lighting);
                bool practice=stage=="circuit-garden";
                _root.StartLocal(new SessionSettings(Stage:stage,Bots:practice?0:7,RoundSeconds:300,SuddenDeath:false),1,practice:practice,seed:64);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"review matrix enters "+stage,180);
                Check(_root.Preferences.Graphics==preset&&LightingArena.TimeOfDay==lighting,"review matrix applies requested preset and lighting");
                Check(Mathf.IsEqualApprox(GetViewport().Scaling3DScale,GraphicsQuality.RenderScale),"review matrix retains actual preset render scale");
                Check(State.GetProperty("players").GetArrayLength()==(practice?1:8),"review matrix uses the declared participant count");
                CheckFraming(stage,GetWindow().Size);
                async Task Save(string moment)
                {
                    string label=$"review-{stage}-{GraphicsQuality.ToWire(preset)}-{StageLighting.ToWire(lighting)}-{moment}";
                    await Capture(label);
                    rows.Add(new{stage,preset=GraphicsQuality.ToWire(preset),lighting=StageLighting.ToWire(lighting),moment,practice,
                        file=Path.GetFileName(_screenshots[^1]),window=new{width=GetWindow().Size.X,height=GetWindow().Size.Y},
                        renderScale=GetViewport().Scaling3DScale,serverTime=State.GetProperty("serverTime").GetInt64(),frame=_frame});
                    File.WriteAllText(Path.Combine(_output,"stage-review-matrix.json"),JsonSerializer.Serialize(new{
                        schemaVersion=1,scope="Real fresh local sessions; same seed and ordinary battle camera. Ambient shader phase may differ; not pixel-equivalence or FPS evidence.",
                        seed=64,stages=stages.Length,rows},new JsonSerializerOptions{WriteIndented=true}));
                }
                await Save("opening");
                if(!practice){await Frames(90);await Save("combat");}
                Input(0,default);_root.Back();await Frames(3);
            }
            Check(rows.Count==(trainingOnly?6:126),"every selected map has its declared preset, lighting and session views");
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");
            await SelectGraphics(originalPreset);await SelectLocalLighting(originalLighting);
            if(GetWindow().Size!=originalSize)await ResizeWindow(originalSize);
        }
    }

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
        foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap","field-of-grass","in-the-gutter","sea-sick","lost-at-sea","circuit-garden"})
        {
            bool practice=stage=="circuit-garden";
            _root.StartLocal(new SessionSettings(Stage:stage,Bots:practice?0:7),1,practice:practice);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"gallery enters "+stage,180);
            await Frames(6);
            var view=_root.GetViewport();
            Check(_root.Preferences.Graphics==GraphicsPreset.High&&view.Msaa3D==Viewport.Msaa.Msaa2X&&Mathf.IsEqualApprox(view.Scaling3DScale,1),"gallery uses native resolution and High 2x AA: "+stage);
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
