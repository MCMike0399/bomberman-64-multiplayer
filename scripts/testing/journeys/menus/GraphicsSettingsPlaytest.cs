using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using BlastCircuit.App;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task GraphicsSettings()
    {
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
        Check(views.All(v=>v.Msaa3D==Viewport.Msaa.Msaa4X),"default quality antialiases every visible menu world at 4x");
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
            }
            foreach(var size in new[]{new Vector2I(1024,640),new Vector2I(1280,800)})
            {
                await ResizeWindow(size);await Frames(5);CheckMenuSampling();
                await ActivateNamed("OpenSettings");await Frames(3);
                AuditVisibleMenu("Settings",size);await Capture("graphics-settings-"+size.X);
                await KeyPress(Key.Escape);
            }
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
                    Check(view.Msaa3D==(preset==GraphicsPreset.Low?Viewport.Msaa.Msaa2X:Viewport.Msaa.Msaa4X),"arena antialiasing follows "+preset);
                    Check(Mathf.IsEqualApprox(view.Scaling3DScale,preset==GraphicsPreset.Low?.85f:1),"arena resolution follows "+preset);
                    Check(environment.SsaoEnabled==(preset!=GraphicsPreset.Low)&&environment.GlowEnabled==(preset!=GraphicsPreset.Low),"arena effects follow "+preset);
                    Check(!view.UseTaa&&view.ScreenSpaceAA==Viewport.ScreenSpaceAAEnum.Disabled,"sharp non-temporal AA policy");
                    Check(environment.SsilEnabled==(preset==GraphicsPreset.High),"indirect bounce is High-only at "+preset);
                    var depthCues=(CameraAttributesPractical)view.GetCamera3D()!.Attributes;
                    Check(depthCues.DofBlurFarEnabled==(preset!=GraphicsPreset.Low)&&depthCues.DofBlurNearEnabled==(preset!=GraphicsPreset.Low),"diorama depth of field follows "+preset);
                    var playable=LightingArena.PlayableDepth;
                    Check(depthCues.DofBlurFarDistance>playable.Y&&depthCues.DofBlurNearDistance<playable.X&&environment.FogDepthBegin>playable.Y,
                        $"blur and haze stay outside the playable depth {playable} on {stage}");
                    CheckPlayableHaze(stage);
                    var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
                    var materials=AuditDescendants(terrain).OfType<MeshInstance3D>().Select(n=>n.MaterialOverride)
                        .OfType<ShaderMaterial>().Where(m=>m.Shader.Code.Contains("uniform float quality_detail")).Distinct().ToArray();
                    Check(materials.Length>0&&materials.All(m=>m.GetShaderParameter("quality_detail").AsSingle()==(preset==GraphicsPreset.Low?0:1)),"live terrain shader detail follows preset on "+stage);
                    var water=terrain.GetChildren().OfType<MeshInstance3D>().Where(n=>n.Mesh is PlaneMesh).ToArray();
                    if(stage is "rock-garden" or "blizzard-battle")Check(water.Length==0,"garden preserves its dry grass setting at "+preset);
                    else if(stage is not ("top-rules" or "greedy-trap"))
                    {
                        Check(water.Length==1,"sea arena retains one water surface: "+stage);
                        Check(((ShaderMaterial)water[0].MaterialOverride).GetShaderParameter("quality_detail").AsSingle()==(preset==GraphicsPreset.Low?0:1),"live water detail follows preset");
                        var waterShader=((ShaderMaterial)water[0].MaterialOverride).Shader;
                        Check(waterShader==ArtMaterials.WaterShaderFor(preset)&&waterShader.Code.Contains("#define CLEAR_WATER")==(preset!=GraphicsPreset.Low),
                            "clear refracting water is used only above Low: "+preset);
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
            while(_root.GraphicsPreparing&&warmup.Elapsed.TotalSeconds<20)await Frames(1);
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
