using Godot;
using BlastCircuit.Rendering;
using Bomber.Client;
using System.Diagnostics;
using System.Text.Json;
namespace Bomber.Testing;

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
        await ResizeWindow(new Vector2I(1920,1200));
        var original=_root.Preferences.Graphics;
        var measurements=new List<object>();
        try
        {
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            {
                _root.Navigate("Modes");await SelectGraphics(preset);
                measurements.Add(await BenchmarkWindow("menu",preset,3));
                await Capture("benchmark-menu-"+GraphicsQuality.ToWire(preset));
                // A 60-second round starts the final-minute flood immediately.
                // Keep the timed workload in ordinary combat for comparable presets.
                _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:7,Wins:3,RoundSeconds:120),1);
                var ready=Stopwatch.StartNew();
                while((_root.LatestState is not {} state||state.GetProperty("phase").GetInt32()!=1)&&ready.Elapsed.TotalSeconds<10)
                    await Frames(1);
                Check(_root.ActiveSession is LocalSession&&State.GetProperty("phase").GetInt32()==1&&State.GetProperty("players").GetArrayLength()==8,
                    "benchmark runs real eight-player local authority");
                measurements.Add(await BenchmarkWindow("eight-player-temple",preset,10));
                await Capture("benchmark-battle-"+GraphicsQuality.ToWire(preset));
                _root.Back();await Frames(3);
            }
        }
        finally {_root.Navigate("Modes");await SelectGraphics(original);}
        var receipt=new
        {
            schemaVersion=1,cpu=OS.GetProcessorName(),gpu,api=RenderingServer.GetVideoAdapterApiVersion(),
            os=OS.GetName(),osVersion=OS.GetVersion(),window=GetWindow().Size.ToString(),
            timingMode="realtime-uncapped",vsync=DisplayServer.WindowGetVsyncMode().ToString(),
            screenRefreshHz=DisplayServer.ScreenGetRefreshRate(),
            seed=64,warmupSeconds=3,measurements,
            limits="Windowed native hardware; diagnostics add CPU overhead. Root GPU timings cover the arena/UI, not independent menu surfaces. Unavailable GPU counters are null. Not a sustained thermal or other-platform benchmark."
        };
        File.WriteAllText(Path.Combine(_output,"graphics-benchmark.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions {WriteIndented=true}));
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
