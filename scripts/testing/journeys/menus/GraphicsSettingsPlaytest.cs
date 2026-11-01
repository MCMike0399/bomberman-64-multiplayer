using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using BlastCircuit.App;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.UI.Menu;
using BlastCircuit.InputDevices;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task HudRedrawCaching()
    {
        var token=new BlastCircuit.UI.Hud.PlayerToken {Size=new(180,98),Position=new(20,300)};
        int draws=0,portraits=0;token.Draw+=()=>draws++;
        _root.AddChild(token);
        var portrait=token.GetChildren().OfType<BomberPortrait>().Single();portrait.Draw+=()=>portraits++;
        try
        {
            token.Refresh();await Frames(3);int initial=draws,initialPortraits=portraits;
            for(int i=0;i<12;i++){token.Refresh();await Frames(1);}
            Check(draws==initial&&portraits==initialPortraits,"unchanged HUD snapshots reuse token and portrait draw commands");
            foreach(var change in new Action[]{()=>token.Tag="Player two",()=>token.PlayerName="Haste 4s",()=>token.Bombs=4,
                ()=>token.Fire=5,()=>token.Red=true,()=>token.Remote=true,()=>token.Heart=true,()=>token.BombType="bounce",
                ()=>token.Focus=true,()=>token.WinTarget=3,()=>token.Status=BlastCircuit.UI.Hud.TokenStatus.Held})
            {
                int before=draws;change();token.Refresh();await Frames(2);
                Check(draws>before,"changed HUD field redraws the visible token");
            }
            int beforePortrait=portraits;token.Suit=Colors.Red;token.Refresh();await Frames(2);
            Check(portraits>beforePortrait&&portrait.Suit==Colors.Red,"changed suit redraws the portrait");
            foreach(var status in new[]{BlastCircuit.UI.Hud.TokenStatus.Ghost,BlastCircuit.UI.Hud.TokenStatus.Out,BlastCircuit.UI.Hud.TokenStatus.Alive})
            {
                beforePortrait=portraits;token.Status=status;token.Refresh();await Frames(2);
                Check(portraits>beforePortrait&&portrait.Ghost==(status==BlastCircuit.UI.Hud.TokenStatus.Ghost)&&portrait.Eliminated==(status==BlastCircuit.UI.Hud.TokenStatus.Out),"portrait follows ghost, elimination and respawn");
            }
            token.Wins=0;token.Refresh();await Frames(2);int beforeWin=draws;
            token.Wins=1;token.Refresh();await Frames(6);
            Check(draws>beforeWin+1,"winning star continues animating between snapshots");
            await Frames(30);int settled=draws;token.Refresh();await Frames(3);
            Check(draws==settled,"winning star stops redrawing after settling");
            token.Size+=new Vector2(10,0);await Frames(2);
            Check(draws>settled,"native size changes invalidate cached HUD commands");
        }
        finally {token.QueueFree();await Frames(2);}
    }

    private async Task DisplaySettings()
    {
        Check(!_headless,"display settings require a native graphical window");
        if(OS.GetCmdlineUserArgs().Contains("--playtest-restore-display"))
        {
            Check(!_root.Preferences.Fullscreen&&_root.Preferences.Resolution==new Vector2I(1280,800),"fresh game process restores saved display choices");
            await DisplaySize(new(1280,800));await ActivateNamed("OpenSettings");await Frames(4);
            Check(!DisplayModeController.Current.Fullscreen&&DisplayModeController.Current.Resolution==new Vector2I(1280,800),"startup applies restored choices to display controller");
            await Capture("display-restored-next-launch");return;
        }
        _root.Navigate("Modes");await ControllerFocusReady();
        await ActivateNamed("OpenSettings");await Frames(4);
        var display=DisplayModeController.Current;
        Check(_root.Preferences.Fullscreen,"missing display preference defaults to fullscreen");
        Check(_root.Preferences.Resolution==Vector2I.Zero,"missing resolution defaults to Automatic");
        Check(GetWindow().Mode==Window.ModeEnum.Windowed,"QA window override preserves fullscreen preference");
        await ActivateNamed("DisplayMode");await WindowMode(Window.ModeEnum.Windowed,"settings selects windowed mode");
        Check(!PlayerPreferences.Load(_root.PreferencesPath).Fullscreen,"windowed preference survives reload");
        foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1280,800)})
        {
            await ActivateNamed("DisplayResolution");await DisplaySize(size);
            Check(PlayerPreferences.Load(_root.PreferencesPath).Resolution==size,"resolution survives reload: "+size);
            AuditVisibleMenu("Settings",size);await Capture("display-settings-"+size.X+"x"+size.Y);
        }
        await KeyPress(Key.Escape);
        _root.StartLocal(new SessionSettings(Bots:0),1,practice:true);await Frames(15);
        var session=_root.ActiveSession;
        await KeyPress(Key.Escape);await ActivateNamed("PauseSettings");
        int snapshots=_root.SnapshotCount;
        await ActivateNamed("DisplayMode");await WindowMode(Window.ModeEnum.Fullscreen,"settings enters real fullscreen");
        await Frames(10);
        float expected=Math.Min(1,Math.Min(1280f/GetWindow().Size.X,800f/GetWindow().Size.Y));
        Check(Mathf.IsEqualApprox(GetWindow().Scaling3DScale,expected),"fullscreen resolution scales actual 3D output");
        var render=DisplayModeController.Current.RenderSize;
        Check(Math.Abs(render.X-GetWindow().Size.X*expected)<=1&&Math.Abs(render.Y-GetWindow().Size.Y*expected)<=1,"settings report the actual 3D output size");
        Check(SettingsText().Contains($"3D renders at {render.X} x {render.Y}"),"settings show the applied 3D resolution");
        var screen=DisplayModeController.Current.ScreenPixels;
        var offered=DisplayModeController.Current.AvailableResolutions();
        Check(offered[0]==Vector2I.Zero&&offered.Skip(1).All(size=>size.X<=screen.X&&size.Y<=screen.Y),"resolution choices never exceed the screen: "+screen);
        Check(DisplayModeController.Resolutions.Where(size=>size.X>screen.X||size.Y>screen.Y).All(size=>!offered.Contains(size)),"sizes beyond the screen are not offered");
        Check(ReferenceEquals(session,_root.ActiveSession)&&snapshots==_root.SnapshotCount,"display change preserves paused local session");
        Check(PlayerPreferences.Load(_root.PreferencesPath).Fullscreen,"fullscreen selection persists");
        await Capture("display-settings-fullscreen");
        // One Esc press, recording every screen it passes through.
        async Task<(List<string> Screens,bool Armed)> EscapeScreens(string label)
        {
            var screens=new List<string>{_root.ScreenName};int window=GetWindow().GetWindowId();int before=_root.TooltipEscapesArmed;
            Godot.Input.ParseInputEvent(new InputEventKey {WindowId=window,Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=true});
            // ParseInputEvent is buffered until the next frame's flush, and in ordering (b) that same
            // dispatch already retires the flag, so count arms once the press has been processed.
            bool pendingAfterPress=false;
            for(int i=0;i<10;i++)
            {
                await Frames(1);if(i==0)pendingAfterPress=_root.TooltipEscapePending;
                if(i==0)Godot.Input.ParseInputEvent(new InputEventKey {WindowId=window,Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=false});
                if(screens[^1]!=_root.ScreenName)screens.Add(_root.ScreenName);
            }
            bool armed=_root.TooltipEscapesArmed==before+1;
            Event("tooltip-escape-screens",new {label,screens,armed,pendingAfterPress,pendingAfter=_root.TooltipEscapePending});
            return (screens,armed);
        }
        // Leak check: Godot's Popup tracks visible parent windows by connecting their focus_entered
        // and tree_exited. On Linux X11 its tooltip teardown logs disconnect messages (allowlisted
        // exactly in tools/playtest/contracts.py), so prove the root Window's connections return
        // to this baseline and no tooltip panel survives.
        var rootWindow=GetTree().Root;
        int Connections(string signal)=>rootWindow.GetSignalConnectionList(signal).Count;
        var parentBaseline=(Focus:Connections("focus_entered"),Exited:Connections("tree_exited"));
        // Ordering (b): the tooltip vanished before GUI input, so the press that armed the
        // deferred action reaches _UnhandledInput in the same frame. A real embedded panel would
        // take the key itself, so the playtest reports a tooltip for exactly this one press.
        _root.TooltipShowingForPlaytest=()=>true;
        List<string> unhandledScreens;bool unhandledArmed;
        try {(unhandledScreens,unhandledArmed)=await EscapeScreens("same-press-reaches-unhandled");}
        finally {_root.TooltipShowingForPlaytest=null;}
        var unhandled=(Screens:unhandledScreens,Armed:unhandledArmed);
        Check(unhandled.Armed,"a tooltip Esc arms the deferred action");
        Check(unhandled.Screens.SequenceEqual(new[]{"Settings","Pause"}),"Esc reaching _UnhandledInput navigates exactly once: "+string.Join(">",unhandled.Screens));
        Check(!_root.TooltipEscapePending,"the deferred action is retired once the same press acted");
        await ActivateNamed("PauseSettings");await Frames(4);
        // Ordering (a), the real case: real fullscreen leaves the OS pointer resting over the menu. On the Windows GPU
        // run (41214884627) the frame-cap tooltip was showing and Esc did not leave
        // Settings. Esc must leave Settings in one press even with a tooltip shown.
        var cap=(Control)_root.FindChild("FrameCap",true,false)!;
        cap.GetViewport().WarpMouse(cap.GetGlobalTransformWithCanvas()*(cap.Size/2));await Frames(2);
        var hover=cap.GetViewport().GetScreenTransform()*cap.GetGlobalTransformWithCanvas()*(cap.Size/2);
        Godot.Input.ParseInputEvent(new InputEventMouseMotion {Position=hover,GlobalPosition=hover});
        bool TooltipShown()=>AuditDescendants(GetTree().Root).OfType<Label>().Any(label=>label.IsVisibleInTree()&&label.Text==cap.TooltipText);
        var tooltipWait=System.Diagnostics.Stopwatch.StartNew();
        while(!TooltipShown()&&tooltipWait.Elapsed.TotalSeconds<5)await Frames(1);
        Check(TooltipShown(),"frame-cap tooltip shows under a resting pointer");
        await Capture("display-settings-tooltip");
        var tip=AuditDescendants(GetTree().Root).OfType<Label>().First(label=>label.IsVisibleInTree()&&label.Text==cap.TooltipText);
        var chain=new List<string>();for(Node? n=tip;n!=null;n=n.GetParent())chain.Add(n.GetClass()+":"+n.Name+(n is Window w?"[embedded="+w.IsEmbedded()+",popup="+w.GetFlag(Window.Flags.Popup)+",nofocus="+w.GetFlag(Window.Flags.NoFocus)+",focus="+w.HasFocus()+"]":""));
        Event("tooltip-escape",new {activePopup=DisplayServer.WindowGetActivePopup(),mainWindow=GetWindow().GetWindowId(),screen=_root.ScreenName,chain});
        var dismissed=await EscapeScreens("gui-dismisses-tooltip");
        Check(dismissed.Armed,"Esc with a tooltip defers its action while the GUI dismisses the tooltip");
        Check(dismissed.Screens.SequenceEqual(new[]{"Settings","Pause"}),"Esc leaves Settings in one press while a tooltip shows, exactly once: "+string.Join(">",dismissed.Screens));
        Check(!TooltipShown(),"leaving Settings dismisses the tooltip");
        Check(!_root.TooltipEscapePending,"the deferred tooltip Esc ran once");
        await ActivateNamed("ResumeGame");await Frames(20);
        Check(ReferenceEquals(session,_root.ActiveSession)&&_root.SnapshotCount>snapshots,"same local session resumes after display change");
        CheckFraming("sunken-temple",GetWindow().Size);await Capture("display-fullscreen-arena");
        await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Windowed,"F11 returns to selected window size");
        await DisplaySize(new(1280,800));
        Check(!_root.Preferences.Fullscreen&&!PlayerPreferences.Load(_root.PreferencesPath).Fullscreen,"F11 updates saved display mode");
        await Frames(5);
        var parentAfter=(Focus:Connections("focus_entered"),Exited:Connections("tree_exited"));
        var tooltipPanels=cap.GetChildren(true).OfType<PopupPanel>().Where(GodotObject.IsInstanceValid).ToArray();
        Event("popup-parent-connections",new {baselineFocus=parentBaseline.Focus,baselineExited=parentBaseline.Exited,
            afterFocus=parentAfter.Focus,afterExited=parentAfter.Exited,tooltipPanels=tooltipPanels.Length});
        Check(parentAfter==parentBaseline,"root Window focus_entered/tree_exited connections return to baseline after the tooltip and F11 ("
            +parentBaseline+" -> "+parentAfter+")");
        Check(tooltipPanels.Length==0,"no tooltip panel remains visible or alive under the frame cap");
        await KeyPress(Key.Enter,true);await WindowMode(Window.ModeEnum.Fullscreen,"Alt+Enter restores fullscreen");
        await KeyPress(Key.Enter,true);await WindowMode(Window.ModeEnum.Windowed,"Alt+Enter returns to windowed");
        _root.Back();await Frames(4);await ActivateNamed("OpenSettings");
        Check(!display.Fullscreen,"shortcuts and settings share one display state");
        await VerifyDisplayRestart();
        // Wrap to Automatic through ordinary input; a smaller display must safely fit a saved 4K window.
        for(int i=0;i<DisplayModeController.Resolutions.Length&&_root.Preferences.Resolution!=Vector2I.Zero;i++)
        {
            await ActivateNamed("DisplayResolution");
            var usable=DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen);
            Check(GetWindow().Size.X<=usable.Size.X&&GetWindow().Size.Y<=usable.Size.Y,"oversized saved window fits current display");
        }
        await DisplaySize(new(1280,800));
        var path=_root.PreferencesPath+".display-invalid";
        try
        {
            var file=new ConfigFile();file.SetValue("video","fullscreen","invalid");file.SetValue("video","resolution",new Vector2I(99999,-4));file.Save(path);
            var loaded=PlayerPreferences.Load(path);
            Check(loaded.Fullscreen&&loaded.Resolution==Vector2I.Zero,"invalid saved display choices safely use defaults");
            file.SetValue("video","resolution","1920x1080");file.Save(path);
            Check(PlayerPreferences.Load(path).Resolution==Vector2I.Zero,"malformed resolution safely uses Automatic");
        }
        finally {DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));}
        await Capture("display-settings-automatic");await KeyPress(Key.Escape);
    }

    private Task VerifyDisplayRestart()
    {
        string id=Guid.NewGuid().ToString("N");
        string path=ProjectSettings.GlobalizePath($"user://qa-preferences-{id}.cfg");
        File.Copy(ProjectSettings.GlobalizePath(_root.PreferencesPath),path);
        string output=Path.Combine(_output,"restart");Directory.CreateDirectory(output);
        // The external runner launches the exact executable after this process
        // exits, so only one game owns the renderer. It requires the child's
        // complete restoration receipt, with the same 90-second deadline.
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(output,"request.json"),JsonSerializer.Serialize(new {fixtureId=id,preferencesPath=path,preferencesSha256=hash}));
        _displayRestartRequested=true;
        Check(File.Exists(path),"saved display choices are retained for the next executable process");
        Event("display-restart-request",new {fixtureId=id,preferencesSha256=hash});
        return Task.CompletedTask;
    }

    private string SettingsText()=>string.Join("\n",AuditDescendants(_root).OfType<Label>().Where(label=>label.IsVisibleInTree()).Select(label=>label.Text));

    private async Task DisplaySize(Vector2I size)
    {
        var wall=System.Diagnostics.Stopwatch.StartNew();double? since=null;
        while(wall.Elapsed.TotalSeconds<15)
        {
            await Frames(1);await Task.Delay(20);
            if(GetWindow().Size==size)
            {
                since??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-since>=.5)break;
            }
            else since=null;
        }
        Check(since.HasValue&&wall.Elapsed.TotalSeconds-since>=.5,"product applies stable window resolution: "+size);
    }

    private async Task RenderSurfaceStartup()
    {
        Check(!_headless,"menu surface startup requires the real renderer");
        var quality=_root.Preferences.Graphics;
        await SelectGraphics(GraphicsPreset.High);
        var backdrop=new ArcadeBackdrop {Name="StartupLayoutProbe",Size=Vector2.Zero,ProcessMode=ProcessModeEnum.Disabled};
        _root.AddChild(backdrop);
        try
        {
            var viewport=backdrop.GetChildren().OfType<SubViewport>().Single();
            var authored=new Vector2I(1440,900);
            Event("pending-layout-render",new {size=viewport.Size.ToString()});
            // Startup preparation can draw before the parent Control is laid out.
            // Exercise High's real contact shading and water in that exact state.
            await backdrop.PrepareArenas(new[]{"up-and-down"});
            Check(viewport.Size==authored,"an unlaid-out menu preserves its authored render target");
            backdrop.Size=Vector2.One;backdrop._Process(0);
            Check(viewport.Size==authored,"a transient one-pixel layout preserves a valid render target");
            var transform=backdrop.GetViewport().GetScreenTransform()*backdrop.GetGlobalTransformWithCanvas();
            backdrop.Size=new(32/transform.X.Length(),32/transform.Y.Length());backdrop._Process(0);
            await backdrop.PrepareArenas(new[]{"up-and-down"});
            using(var smallest=viewport.GetTexture().GetImage())
                Check(smallest.GetWidth()==32&&smallest.GetHeight()==32,"the smallest accepted target renders High contact shading safely");
            backdrop.Size=new(640,400);backdrop._Process(0);
            var pixels=backdrop.Size*new Vector2(transform.X.Length(),transform.Y.Length());
            Check(viewport.Size==new Vector2I(Mathf.RoundToInt(pixels.X),Mathf.RoundToInt(pixels.Y)),"settled menu layout resumes output-pixel sizing");
            await backdrop.PrepareArenas(new[]{"up-and-down","blizzard-battle"});
            using var image=viewport.GetTexture().GetImage();
            Check(!image.IsEmpty()&&image.GetWidth()==viewport.Size.X&&image.GetHeight()==viewport.Size.Y,"the settled menu renders its real water and ice miniatures");
            await Capture("render-surface-layout-settled");
        }
        finally {_root.RemoveChild(backdrop);backdrop.QueueFree();await SelectGraphics(quality);}
    }

    private async Task GraphicsSettings()
    {
        await HudRedrawCaching();
        _root.Navigate("Modes");
        await ResizeWindow(new Vector2I(1600,1000));
        await Frames(60);
        var plaques=AuditDescendants(_root).OfType<ArcadeModeButton>().ToArray();
        int before=plaques.Sum(p=>p.RenderRequests);
        await Frames(30);
        Event("graphics-idle",new {frames=30,plaqueRedraws=plaques.Sum(p=>p.RenderRequests)-before});
        var views=AuditDescendants(_root).OfType<SubViewport>()
            .Where(v=>v.GetParent() is Control c&&c.IsVisibleInTree()).ToArray();
        foreach(var view in views)
        {
            var c=(Control)view.GetParent();
            var transform=c.GetViewport().GetScreenTransform()*c.GetGlobalTransformWithCanvas();
            var physical=c.Size*new Vector2(transform.X.Length(),transform.Y.Length());
            Event("graphics-viewport",new {owner=c.GetType().Name,size=view.Size.ToString(),physical=physical.ToString(),msaa=view.Msaa3D.ToString()});
        }
        await Capture("graphics-menu-default");
        Check(views.All(v=>v.Msaa3D==Viewport.Msaa.Msaa2X),"default quality antialiases every visible menu world at 2x");
        Check(plaques.Sum(p=>p.RenderRequests)-before<=6,"settled plaques reuse their rendered texture");
        CheckMenuSampling();
        Check(_root.GetViewport().Disable3D,"opaque menu skips the hidden arena rendering pass");
        await ControllerFocusReady();
        var original=_root.Preferences.Graphics;
        try
        {
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.High,GraphicsPreset.Medium})
            {
                await SelectGraphics(preset);
                CheckMenuSampling();
                await Capture("graphics-menu-"+GraphicsQuality.ToWire(preset));
                // Each preset description wraps differently. Every preset must keep the whole
                // Settings screen, including the Windows renderer rows, in the minimum window;
                // Medium is audited again by the size loop below.
                if(preset!=GraphicsPreset.Medium)
                {
                    var restore=GetWindow().Size;await ResizeWindow(new(1024,640));await Frames(5);
                    await ActivateNamed("OpenSettings");await Frames(3);
                    await AuditSettingsFit(new(1024,640),preset);
                    await KeyPress(Key.Escape);await ResizeWindow(restore);await Frames(5);
                }
            }
            foreach(var size in new[]{new Vector2I(1024,640),new Vector2I(1280,800)})
            {
                await ResizeWindow(size);await Frames(5);CheckMenuSampling();
                await ActivateNamed("OpenSettings");await Frames(3);
                if(size.X==1024)await AuditSettingsFit(size,GraphicsPreset.Medium);else AuditVisibleMenu("Settings",size);
                await Capture("graphics-settings-"+size.X);
                await KeyPress(Key.Escape);
            }
            // After both reviewed captures: walking focus changes dial highlight state.
            await ResizeWindow(new(1024,640));await Frames(5);
            await ActivateNamed("OpenSettings");await Frames(3);
            await CheckNowReachable(new(1024,640));
            await KeyPress(Key.Escape);await ResizeWindow(new(1280,800));await Frames(5);
            foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap","circuit-garden"})
            {
                _root.StartLocal(new SessionSettings(Stage:stage,Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"graphics enters "+stage,180);
                foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
                {
                    await SelectGraphics(preset);
                    var view=_root.GetViewport();
                    Check(!view.Disable3D,"match restores arena rendering");
                    var environment=AuditDescendants(LightingArena).OfType<WorldEnvironment>().Single().Environment;
                    Check(view.Msaa3D==Viewport.Msaa.Msaa2X,"arena antialiasing is 2x at "+preset);
                    Check(Mathf.IsEqualApprox(view.Scaling3DScale,preset==GraphicsPreset.Low?.75f:1),"arena resolution follows "+preset);
                    Check(environment.SsaoEnabled==(preset!=GraphicsPreset.Low)&&environment.GlowEnabled==(preset!=GraphicsPreset.Low),"arena effects follow "+preset);
                    Check(!view.UseTaa&&view.ScreenSpaceAA==Viewport.ScreenSpaceAAEnum.Disabled,"sharp non-temporal AA policy");
                    Check(environment.SsilEnabled==(preset==GraphicsPreset.High),"indirect bounce is High-only at "+preset);
                    Check(!environment.SsrEnabled,"opaque screen-space reflections stay off at "+preset+" on "+stage);
                    var sun=AuditDescendants(LightingArena).OfType<DirectionalLight3D>().Single(l=>l.ShadowEnabled);
                    Check((sun.LightAngularDistance>0)==(preset==GraphicsPreset.High),"contact-hardening sun shadows are High-only at "+preset);
                    var depthCues=(CameraAttributesPractical)view.GetCamera3D()!.Attributes;
                    Check(depthCues.DofBlurFarEnabled==(preset==GraphicsPreset.High)&&depthCues.DofBlurNearEnabled==(preset==GraphicsPreset.High),"diorama depth of field is High-only at "+preset);
                    var playable=LightingArena.PlayableDepth;
                    Check(depthCues.DofBlurFarDistance>playable.Y&&depthCues.DofBlurNearDistance<playable.X&&environment.FogDepthBegin>playable.Y,
                        $"blur and haze stay outside the playable depth {playable} on {stage}");
                    CheckPlayableHaze(stage);
                    var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
                    var materials=AuditDescendants(terrain).OfType<MeshInstance3D>().Select(n=>n.MaterialOverride)
                        .OfType<ShaderMaterial>().Where(m=>m.Shader.Code.Contains("uniform float quality_detail")).Distinct().ToArray();
                    Check(materials.Length>0&&materials.All(m=>m.GetShaderParameter("quality_detail").AsSingle()==(preset==GraphicsPreset.Low?0:1)),"live terrain shader detail follows preset on "+stage);
                    var premium=materials.Where(m=>m.Shader.Code.Contains("uniform float quality_premium")).ToArray();
                    Check(premium.Length>0&&premium.All(m=>m.GetShaderParameter("quality_premium").AsSingle()==(preset==GraphicsPreset.High?1:0)),"High-only stage embellishments follow preset on "+stage);
                    var water=terrain.GetChildren().OfType<MeshInstance3D>().Where(n=>PrimitiveTemplates.Is(n.Mesh,"PlaneMesh")).ToArray();
                    if(stage is "rock-garden" or "blizzard-battle")Check(water.Length==0,"garden preserves its dry grass setting at "+preset);
                    else if(stage is not ("top-rules" or "greedy-trap"))
                    {
                        Check(water.Length==1,"sea arena retains one water surface: "+stage);
                        Check(((ShaderMaterial)water[0].MaterialOverride).GetShaderParameter("quality_detail").AsSingle()==(preset==GraphicsPreset.Low?0:1),"live water detail follows preset");
                        var waterShader=((ShaderMaterial)water[0].MaterialOverride).Shader;
                        Check(waterShader==ArtMaterials.WaterShaderFor(preset)&&waterShader.Code.Contains("#define CLEAR_WATER")==(preset!=GraphicsPreset.Low),
                            "clear refracting water is used only above Low: "+preset);
                        Check(waterShader.Code.Contains("#define SCENE_REFLECTIONS")==(preset==GraphicsPreset.High),"scene reflections in water are High-only: "+preset);
                        Check(waterShader.Code.Contains("#define WATER_SPARKLE")==(preset==GraphicsPreset.High),"caustic webs and sun glints are High-only: "+preset);
                    }
                    await Capture("graphics-"+stage+"-"+GraphicsQuality.ToWire(preset));
                    await MeasureGraphics(stage,preset);
                }
                var camera=_root.GetViewport().GetCamera3D()!;
                var pose=camera.Transform;
                try
                {
                    // An oblique inspection view separates radial fog distance from
                    // axial DOF depth. Use actual terrain, including lower passages.
                    camera.Position=new(12.5f,15,18);camera.LookAt(new Vector3(12.5f,1,14));
                    await Frames(3);
                    CheckPlayableHaze(stage);
                }
                finally {camera.Transform=pose;}
                _root.Back();await Frames(3);
            }
            var path=_root.PreferencesPath+".invalid";
            try
            {
                var file=new ConfigFile();file.SetValue("video","graphicsPreset","future-unknown");file.Save(path);
                Check(PlayerPreferences.Load(path).Graphics==GraphicsPreset.Medium,"unknown saved preset falls back to Medium");
                file.SetValue("video","graphicsPreset",42);file.Save(path);
                Check(PlayerPreferences.Load(path).Graphics==GraphicsPreset.Medium,"malformed saved preset falls back to Medium");
            }
            finally {DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));}
            var art=new ArtMaterials();var first=art.Stone();var second=art.Masonry();
            Check(first.Shader==second.Shader,"terrain shares compiled stone shader");
            first.SetShaderParameter("water_height",3);
            Check(second.GetShaderParameter("water_height").AsSingle()!=3,"shared shader keeps per-scene material uniforms independent");
        }
        finally {_root.Navigate("Modes");await SelectGraphics(original);}
        await ResizeWindow(new Vector2I(1280,800));
    }

    private void CheckPlayableHaze(string stage)
    {
        var camera=_root.GetViewport().GetCamera3D()!;
        var environment=AuditDescendants(LightingArena).OfType<WorldEnvironment>().Single().Environment;
        var data=_root.LatestStage!.Value;var cells=data.GetProperty("cells");
        int width=data.GetProperty("width").GetInt32();float farthest=0;
        var view=camera.GetCameraTransform().AffineInverse();
        for(int i=0;i<cells.GetArrayLength();i++)Sample(cells[i],i%width,i/width);
        void Sample(JsonElement cell,int x,int z)
        {
            if(cell.TryGetProperty("below",out var lower)&&lower.ValueKind==JsonValueKind.Object)Sample(lower,x,z);
            int kind=cell.GetProperty("k").GetInt32();if(kind is 2 or 3)return;
            float y=(kind==4?cell.GetProperty("rt"):cell.GetProperty("t")).GetSingle()*TempleStageView.HeightScale;
            float head=kind==1?(cell.TryGetProperty("oh",out var crown)&&crown.ValueKind==JsonValueKind.Number?crown.GetSingle()*TempleStageView.HeightScale:1.21f):2.1f;
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})foreach(float h in new[]{0f,head})
                farthest=Mathf.Max(farthest,(view*new Vector3(x+dx,y+h,z+dz)).Length());
        }
        Event("playable-haze-distance",new {stage,farthest,begin=environment.FogDepthBegin});
        Check(environment.FogDepthBegin>farthest,$"radial haze stays beyond playable terrain and characters on {stage}: {environment.FogDepthBegin} > {farthest}");
    }

    private void CheckMenuSampling()
    {
        var surfaces=AuditDescendants(_root).OfType<RenderSurface>().Where(s=>s.IsVisibleInTree()).ToArray();
        Check(surfaces.Length>=4,"menu exposes all four visible 3D surfaces");
        foreach(var surface in surfaces)
        {
            var viewport=surface.GetChildren().OfType<SubViewport>().Single();
            var t=surface.GetViewport().GetScreenTransform()*surface.GetGlobalTransformWithCanvas();
            var pixels=surface.Size*new Vector2(t.X.Length(),t.Y.Length());
            Check(Math.Abs(viewport.Size.X-pixels.X)<1.1&&Math.Abs(viewport.Size.Y-pixels.Y)<1.1,"menu texture matches output pixels: "+surface.GetType().Name);
            Check(viewport.Msaa3D==GraphicsQuality.Antialiasing,"menu antialiasing follows preset");
        }
    }

    /// <summary>
    /// Audits Settings in the minimum window. With the Windows renderer rows, it also
    /// changes the renderer so the saved/error status is audited too; that status stays
    /// in the dial's row, so the lowest control must not move.
    /// </summary>
    private async Task AuditSettingsFit(Vector2I size,GraphicsPreset preset)
    {
        float Lowest()=>AuditDescendants(_root).OfType<BaseButton>().Where(c=>c.IsVisibleInTree())
            .Max(c=>(c.GetGlobalTransformWithCanvas()*c.Size).Y);
        AuditVisibleMenu("Settings",size);
        if(!ArcadeMenu.WindowsRendererRows)return;
        float before=Lowest();var dial=NamedButton("RendererChoice");var focus=GetViewport().GuiGetFocusOwner();
        dial.GrabFocus();await KeyPress(Key.Right);await Frames(3);
        var caption=AuditDescendants(dial).OfType<Label>().First().Text;
        Check(caption=="Renderer · on restart","renderer change reports its saved status in the dial row: "+caption);
        AuditVisibleMenu("Settings",size);
        Check(Mathf.IsEqualApprox(Lowest(),before),"renderer status keeps the Settings height under "+preset+": "+before+" -> "+Lowest());
        Event("renderer-status-fit",new {preset=preset.ToString(),window=size.ToString(),lowestBefore=before,lowestAfter=Lowest(),caption});
        await KeyPress(Key.Left);await Frames(3);
        if(GodotObject.IsInstanceValid(focus))focus.GrabFocus();
    }

    /// <summary>"Check now" may sit below the scrolled area; keyboard and controller focus must bring it into view.</summary>
    private async Task CheckNowReachable(Vector2I size)
    {
        var scroll=(Control)_root.FindChild("SettingsScroll",true,false)!;var target=NamedButton("CheckUpdates");
        var first=(Control)_root.FindChild("SoundVolume",true,false)!;
        bool InView()=>scroll.GetGlobalRect().Grow(1).Encloses(target.GetGlobalRect());
        async Task<bool> Reach(Func<Task> step)
        {
            for(int i=0;i<30&&GetViewport().GuiGetFocusOwner()!=target;i++)await step();
            await Frames(6);return GetViewport().GuiGetFocusOwner()==target&&InView();
        }
        first.GrabFocus();await Frames(3);
        Check(await Reach(()=>KeyPress(Key.Tab)),"Tab focus scrolls Check now into view at "+size);
        await Capture("settings-check-now-tab-"+size.X);
        var hub=ControllerHub.Current;CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[0]]);await ControllerFrames(3);
        try
        {
            first.GrabFocus();await ControllerFrames(3);
            async Task Pad(JoyButton button){CtlButton(QaPads[0].RuntimeId,button,true);await ControllerFrames(1);CtlButton(QaPads[0].RuntimeId,button,false);await ControllerFrames(3);}
            await Pad(JoyButton.RightShoulder);
            Check(await Reach(()=>Pad(JoyButton.DpadDown)),"controller focus scrolls Check now into view at "+size);
            await Capture("settings-check-now-controller-"+size.X);
        }
        finally {CtlReleaseAll();hub.OverrideDevicesForPlaytest([]);await ControllerFrames(3);}
        // Leave Settings as it opened so later captures keep their original framing.
        first.GrabFocus();((ScrollContainer)scroll).ScrollVertical=0;await Frames(3);
    }

    private async Task SelectGraphics(GraphicsPreset preset)
    {
        // Screen transitions defer their initial focus; let that settle before
        // focusing and activating a specific native control.
        await Frames(3);
        bool match=_root.ScreenName=="Match";
        if(match){await KeyPress(Key.Escape);await ActivateNamed("PauseSettings");}
        else {await ActivateNamed("OpenSettings");}
        Check(_root.ScreenName=="Settings","graphics opens the settings screen");
        var session=_root.ActiveSession;int snapshots=_root.SnapshotCount;
        var lighting=LightingArena.TimeOfDay;
        var dial=NamedButton("GraphicsPreset");dial.GrabFocus();
        for(int i=0;i<3&&_root.Preferences.Graphics!=preset;i++)
        {
            await KeyPress(Key.Right);
            var warmup=System.Diagnostics.Stopwatch.StartNew();
            // Retained llvmpipe and WARP runs rendered only 14-15 preparation
            // frames in 20 seconds; WARP once needed 182 seconds for 28 frames.
            // Software renderers get frame-equivalent time (see Waiting).
            bool software=SoftwareRenderer;
            double budget=20;
            ulong firstFrame=Engine.GetProcessFrames();
            while(_root.GraphicsPreparing&&Waiting(warmup,budget,firstFrame))await Frames(1);
            var preparation=new {preset=GraphicsQuality.ToWire(preset),seconds=warmup.Elapsed.TotalSeconds,
                frames=Engine.GetProcessFrames()-firstFrame,budget,finished=!_root.GraphicsPreparing,
                software,adapter=RenderingServer.GetVideoAdapterName(),adapterType=RenderingServer.GetVideoAdapterType().ToString()};
            _graphicsPreparations.Add(preparation);
            File.WriteAllText(Path.Combine(_output,"graphics-preparation.json"),JsonSerializer.Serialize(_graphicsPreparations));
            Event("graphics-preparation",preparation);
            Check(!_root.GraphicsPreparing,"graphics preparation finishes before returning control");
            dial.GrabFocus();
        }
        Check(_root.Preferences.Graphics==preset&&PlayerPreferences.Load(_root.PreferencesPath).Graphics==preset,"native graphics dial applies and persists "+preset);
        // Online rounds keep advancing while the graphics pipelines prepare.
        // Their accepted lighting can change legitimately during this interval.
        var expectedLighting=session is NetworkSession&&_root.LatestState is {} state
            ? StageLighting.FromWire(state.GetProperty("stageLighting").GetString()):lighting;
        if(session!=null)Check(ReferenceEquals(session,_root.ActiveSession)&&LightingArena.TimeOfDay==expectedLighting,"graphics changes preserve session and authoritative lighting");
        if(session is LocalSession)Check(_root.SnapshotCount==snapshots,"graphics changes preserve paused local authority");
        await KeyPress(Key.Escape);
        if(match)await ActivateNamed("ResumeGame");
        await Frames(40);
    }

    private async Task MeasureGraphics(string stage,GraphicsPreset preset)
    {
        // GPU time is rendering work, not fixed-step harness FPS or native Mac throughput.
        var viewport=_root.GetViewport();var rid=viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid,true);
        try
        {
            await Frames(15);
            var gpu=new List<double>();var cpu=new List<double>();var calls=new List<int>();
            for(int i=0;i<45;i++)
            {
                await Frames(1);
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
                cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
                calls.Add(viewport.GetRenderInfo(Viewport.RenderInfoType.Visible,Viewport.RenderInfo.DrawCallsInFrame));
            }
            gpu.Sort();cpu.Sort();
            Event("graphics-render-sample",new {stage,preset=preset.ToString(),samples=gpu.Count,
                gpuTimingAvailable=gpu.Any(t=>t>0),gpuMedianMs=gpu[22],gpuP95Ms=gpu[42],renderCpuMedianMs=cpu[22],meanDrawCalls=calls.Average(),
                fixedStep=true,nativeHostBenchmark=false});
        }
        finally {RenderingServer.ViewportSetMeasureRenderTime(rid,false);}
    }
}
