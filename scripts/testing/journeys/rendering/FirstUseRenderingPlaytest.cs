using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using System.Diagnostics;
using BlastCircuit.Rendering;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private static long[] PipelineCounts()=>new[]
    {
        RenderingServer.RenderingInfo.PipelineCompilationsMesh,
        RenderingServer.RenderingInfo.PipelineCompilationsSurface,
        RenderingServer.RenderingInfo.PipelineCompilationsDraw,
        RenderingServer.RenderingInfo.PipelineCompilationsSpecialization
    }.Select(info=>(long)RenderingServer.GetRenderingInfo(info)).ToArray();

    private async Task FirstUseRendering()
    {
        var original=_root.Preferences.Graphics;
        Event("effect-warmup",new {milliseconds=_root.GraphicsWarmupMilliseconds});
        Check(!AuditDescendants(_root).OfType<SubViewport>().Any(v=>v.Name=="EffectWarmup"),"temporary warmup worlds are released before gameplay");
        await ControllerFocusReady();
        try
        {
            foreach(var preset in new[]{GraphicsPreset.Medium,GraphicsPreset.Low,GraphicsPreset.High})
            {
                _root.Navigate("Modes");await SelectGraphics(preset);
                _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"first-use practice reaches fight",180);
                await Frames(30);
                for(int trial=0;trial<2;trial++)
                {
                    // Warm shader resources must survive collection of scratch models.
                    GC.Collect();await Frames(5);
                    int blasts=Cue("blast");var before=PipelineCounts();
                    var frames=new List<double>();var watch=Stopwatch.StartNew();double previous=watch.Elapsed.TotalMilliseconds;
                    Input(0,new PlayerInput(0,0,A:true));
                    for(int i=0;i<152;i++)
                    {
                        await Frames(1);double now=watch.Elapsed.TotalMilliseconds;frames.Add(now-previous);previous=now;
                        if(i==1)Input(0,default);
                    }
                    Check(Cue("blast")==blasts+1,"ordinary bomb completes first-use explosion trial "+preset+" "+trial);
                    var delta=PipelineCounts().Zip(before,(a,b)=>a-b).ToArray();
                    Event("first-use-pipelines",new {preset=preset.ToString(),trial,mesh=delta[0],surface=delta[1],draw=delta[2],backgroundSpecialization=delta[3],
                        worstFrameMs=frames.Max(),medianFrameMs=frames.Order().ElementAt(frames.Count/2),fixedStep=true});
                    Check(delta.Take(3).Sum()==0,"real explosion requires no synchronous pipeline creation: "+preset+" "+trial);
                }
                await Capture("first-use-after-"+GraphicsQuality.ToWire(preset));
                _root.Back();await Frames(3);
            }
        }
        finally {_root.Navigate("Modes");await SelectGraphics(original);}
    }
}
