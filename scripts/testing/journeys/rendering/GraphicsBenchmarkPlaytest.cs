using Godot;
using Bomber.Client.Sessions;
using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.Rendering;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task GraphicsBenchmark()
    {
        string gpu=RenderingServer.GetVideoAdapterName();
        Check(!_headless&&!OS.GetCmdlineArgs().Contains("--fixed-fps")&&Engine.MaxFps==0,
            "native benchmark uses real-time uncapped graphical rendering");
        Check(!new[]{"paravirtual","llvmpipe","swiftshader","software"}.Any(s=>gpu.Contains(s,StringComparison.OrdinalIgnoreCase)),
            "native benchmark rejects virtual/software GPU: "+gpu);
        await ControllerFocusReady();
        var arguments=OS.GetCmdlineUserArgs();
        string Option(string name,string fallback)=>arguments.FirstOrDefault(a=>a.StartsWith(name+"="))?[(name.Length+1)..]??fallback;
        var dimensions=Option("--benchmark-resolution","1920x1200").Split('x');
        var resolution=new Vector2I(int.Parse(dimensions[0]),int.Parse(dimensions[1]));
        string stage=Option("--benchmark-stage","sunken-temple");
        var stages=stage=="all"?new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"}:new[]{stage};
        int seconds=int.Parse(Option("--benchmark-seconds","10"));
        string selected=Option("--benchmark-preset","all");
        string diagnosticOmission=Option("--benchmark-disable","none");
        // Diagnostic A/B: native-resolution captures of a static practice scene before and
        // after each candidate renderer tweak, then a timed bot battle per tweak. The ordinary
        // full-quality measurements are taken untouched before any tweak is applied.
        string tweakList=Option("--benchmark-tweak","none");
        var tweaks=tweakList=="none"||tweakList.Length==0?Array.Empty<string>():tweakList.Split(',',StringSplitOptions.RemoveEmptyEntries);
        var tweakMeasurements=new List<object>();
        var presets=selected=="all"?new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High}:new[]{GraphicsQuality.FromWire(selected)};
        Check(resolution.X is >=100 and <=7680&&resolution.Y is >=100 and <=7680&&seconds is >=10 and <=120,"benchmark workload is bounded");
        // A native-panel-sized window needs no title bar or dock reservation.
        // Keep a real window (and the normal canvas scaling), never an upscaled framebuffer.
        if(resolution.Y>DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen).Size.Y-80)
            GetWindow().Borderless=true;
        await ResizeWindow(resolution);
        var original=_root.Preferences.Graphics;
        var measurements=new List<object>();
        try
        {
            foreach(var preset in presets)
            {
                _root.Navigate("Modes");await SelectGraphics(preset);
                measurements.Add(await BenchmarkWindow("menu",preset,3));
                await Capture("benchmark-menu-"+GraphicsQuality.ToWire(preset));
                foreach(string arena in stages)
                {
                    if(tweaks.Length>0)await CaptureTweakPairs(arena,preset,tweaks);
                    // A 60-second round starts the final-minute flood immediately.
                    // Keep the timed workload in ordinary combat for comparable presets.
                    _root.StartLocal(new SessionSettings(Stage:arena,Bots:7,Wins:3,RoundSeconds:Math.Max(120,seconds+90)),1);
                    var ready=Stopwatch.StartNew();
                    while((_root.LatestState is not {} state||state.GetProperty("phase").GetInt32()!=1)&&ready.Elapsed.TotalSeconds<10)
                        await Frames(1);
                    Check(_root.ActiveSession is LocalSession&&State.GetProperty("phase").GetInt32()==1&&State.GetProperty("players").GetArrayLength()==8,
                        "benchmark runs real eight-player local authority");
                    var environment=AuditDescendants(LightingArena).OfType<WorldEnvironment>().Single().Environment;
                    var attributes=(CameraAttributesPractical)_root.GetViewport().GetCamera3D()!.Attributes;
                    switch(diagnosticOmission)
                    {
                        case "ssil":environment.SsilEnabled=false;break;
                        case "ssao":environment.SsaoEnabled=false;break;
                        case "glow":environment.GlowEnabled=false;break;
                        case "dof":attributes.DofBlurNearEnabled=attributes.DofBlurFarEnabled=false;break;
                        case "shadows":foreach(var light in AuditDescendants(LightingArena).OfType<Light3D>())light.ShadowEnabled=false;break;
                        case "water":foreach(var mesh in AuditDescendants(LightingArena).OfType<MeshInstance3D>())
                            if(mesh.MaterialOverride is ShaderMaterial material&&material.Shader.Code.Contains("uniform float seabed_height"))mesh.Visible=false;break;
                    }
                    string scene=arena=="sunken-temple"?"eight-player-temple":"eight-player-"+arena;
                    measurements.Add(await BenchmarkWindow(scene,preset,seconds));
                    await Capture("benchmark-battle-"+arena+"-"+GraphicsQuality.ToWire(preset));
                    foreach(string tweak in tweaks)
                    {
                        var revert=ApplyTweak(tweak,environment,attributes);
                        try {tweakMeasurements.Add(new {tweak,result=await BenchmarkWindow(scene,preset,seconds)});}
                        finally {revert();}
                    }
                    _root.Back();await Frames(3);
                }
            }
        }
        finally {_root.Navigate("Modes");await SelectGraphics(original);}
        var receipt=new
        {
            schemaVersion=1,cpu=OS.GetProcessorName(),gpu,api=RenderingServer.GetVideoAdapterApiVersion(),
            os=OS.GetName(),osVersion=OS.GetVersion(),window=GetWindow().Size.ToString(),
            timingMode="realtime-uncapped",vsync=DisplayServer.WindowGetVsyncMode().ToString(),
            screenRefreshHz=DisplayServer.ScreenGetRefreshRate(),
            seed=64,warmupSeconds=3,stage,diagnosticOmission,fullQuality=diagnosticOmission=="none",requestedWidth=resolution.X,requestedHeight=resolution.Y,measurements,
            diagnosticTweaks=tweaks,tweakMeasurements,
            limits="Windowed native hardware; diagnostics add CPU overhead. Root GPU timings cover the arena/UI, not independent menu surfaces. Unavailable GPU counters are null. Not a sustained thermal or other-platform benchmark."
        };
        File.WriteAllText(Path.Combine(_output,"graphics-benchmark.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions {WriteIndented=true}));
    }

    /// <summary>Static practice captures at the benchmark resolution: base, each tweak, base again (animation floor).</summary>
    private async Task CaptureTweakPairs(string arena,GraphicsPreset preset,string[] tweaks)
    {
        _root.StartLocal(new SessionSettings(Stage:arena,Bots:0),1,practice:true);
        // Uncapped frames are short: wait on the clock, as the bot battles do.
        var ready=Stopwatch.StartNew();
        while((_root.LatestState is not {} state||state.GetProperty("phase").GetInt32()!=1)&&ready.Elapsed.TotalSeconds<10)await Frames(1);
        Check(_root.LatestState is {} started&&started.GetProperty("phase").GetInt32()==1,"tweak capture enters "+arena);
        await Frames(30);
        var environment=AuditDescendants(LightingArena).OfType<WorldEnvironment>().Single().Environment;
        var attributes=(CameraAttributesPractical)_root.GetViewport().GetCamera3D()!.Attributes;
        string label="benchmark-ab-"+arena+"-"+GraphicsQuality.ToWire(preset);
        await Capture(label+"-base");
        foreach(string tweak in tweaks)
        {
            var revert=ApplyTweak(tweak,environment,attributes);
            try {await Frames(12);await Capture(label+"-"+tweak);}
            finally {revert();await Frames(12);}
        }
        await Capture(label+"-base-again");
        _root.Back();await Frames(3);
    }

    /// <summary>Applies one named renderer tweak and returns the action that restores the previous state.</summary>
    private Action ApplyTweak(string tweak,Godot.Environment environment,CameraAttributesPractical attributes)
    {
        switch(tweak)
        {
            case "sky-incremental":
            {
                var sky=environment.Sky!;var previous=sky.ProcessMode;sky.ProcessMode=Sky.ProcessModeEnum.Incremental;
                return ()=>sky.ProcessMode=previous;
            }
            case "sky-quality":
            {
                var sky=environment.Sky!;var previous=sky.ProcessMode;sky.ProcessMode=Sky.ProcessModeEnum.Quality;
                return ()=>sky.ProcessMode=previous;
            }
            case "dof-verylow":
                RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.VeryLow,true);
                return ()=>RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Low,true);
            case "ssao-medium":
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium,true,.5f,2,50,300);
                return ()=>GraphicsQuality.Apply(GraphicsQuality.Current,_root.GetViewport());
            case "ssao-low":
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Low,true,.5f,2,50,300);
                return ()=>GraphicsQuality.Apply(GraphicsQuality.Current,_root.GetViewport());
            case "ssil-low":
                RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Low,true,.5f,2,50,300);
                return ()=>GraphicsQuality.Apply(GraphicsQuality.Current,_root.GetViewport());
            case "ssil-verylow":
                RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.VeryLow,true,.5f,2,50,300);
                return ()=>GraphicsQuality.Apply(GraphicsQuality.Current,_root.GetViewport());
            default:
                Check(false,"unknown benchmark tweak: "+tweak);return ()=>{};
        }
    }

    private async Task<object> BenchmarkWindow(string scene,GraphicsPreset preset,double seconds)
    {
        var viewport=_root.GetViewport();var rid=viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid,true);
        try
        {
            var warmup=Stopwatch.StartNew();while(warmup.Elapsed.TotalSeconds<3)await Frames(1);
            var frameTimes=new List<double>();var gpuTimes=new List<double>();var cpuTimes=new List<double>();
            var drawCalls=new List<int>();int peakBombs=0,peakHazards=0;
            long allocationsBefore=GC.GetTotalAllocatedBytes();
            int unfocusedFrames=0,nonMatchFrames=0;
            string startUtc=DateTimeOffset.UtcNow.ToString("O");
            var timer=Stopwatch.StartNew();double previous=timer.Elapsed.TotalMilliseconds;
            while(timer.Elapsed.TotalSeconds<seconds)
            {
                await Frames(1);
                if(!GetWindow().HasFocus()||!BlastCircuit.InputDevices.ControllerHub.Current.HasApplicationFocus)unfocusedFrames++;
                if(scene!="menu"&&_root.ScreenName!="Match")nonMatchFrames++;
                double now=timer.Elapsed.TotalMilliseconds;frameTimes.Add(now-previous);previous=now;
                double gpu=RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
                if(gpu>0&&double.IsFinite(gpu))gpuTimes.Add(gpu);
                cpuTimes.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
                drawCalls.Add(viewport.GetRenderInfo(Viewport.RenderInfoType.Visible,Viewport.RenderInfo.DrawCallsInFrame));
                if(scene!="menu"&&_root.LatestState is {} state)
                {
                    peakBombs=Math.Max(peakBombs,state.GetProperty("bombs").GetArrayLength());
                    peakHazards=Math.Max(peakHazards,state.GetProperty("hazards").GetArrayLength());
                }
            }
            string endUtc=DateTimeOffset.UtcNow.ToString("O");
            Check(frameTimes.Count>=30,"benchmark has sufficient rendered frame samples: "+scene+" "+preset);
            Check(unfocusedFrames==0,"native benchmark retained foreground focus: "+scene+" "+preset);
            Check(nonMatchFrames==0,"battle benchmark stayed in the actual match");
            if(scene!="menu")Check(peakBombs>0&&peakHazards>0,"benchmark observes actual bot bombs and explosions: "+preset);
            double Percentile(List<double> samples,double fraction)
            {samples.Sort();return samples[(int)Math.Clamp(Math.Ceiling(samples.Count*fraction)-1,0,samples.Count-1)];}
            var result=new
            {
                scene,preset=preset.ToString(),samples=frameTimes.Count,seconds=timer.Elapsed.TotalSeconds,startUtc,endUtc,
                fps=frameTimes.Count/timer.Elapsed.TotalSeconds,frameMedianMs=Percentile(frameTimes,.5),frameP95Ms=Percentile(frameTimes,.95),
                frameP99Ms=Percentile(frameTimes,.99),frameMaxMs=frameTimes.Max(),
                over120BudgetPercent=100d*frameTimes.Count(ms=>ms>1000d/120)/frameTimes.Count,
                allocatedBytesPerFrame=(GC.GetTotalAllocatedBytes()-allocationsBefore)/(double)frameTimes.Count,
                width=GetWindow().Size.X,height=GetWindow().Size.Y,renderScale=viewport.Scaling3DScale,
                rootGpuMedianMs=gpuTimes.Count>0?(double?)Percentile(gpuTimes,.5):null,
                rootGpuP95Ms=gpuTimes.Count>0?(double?)Percentile(gpuTimes,.95):null,
                rootRenderCpuMedianMs=Percentile(cpuTimes,.5),rootDrawCallsMean=drawCalls.Average(),rootDrawCallsPeak=drawCalls.Max(),
                peakBombs,peakHazards
            };
            Event("native-graphics-benchmark",result);return result;
        }
        finally {RenderingServer.ViewportSetMeasureRenderTime(rid,false);}
    }
}
