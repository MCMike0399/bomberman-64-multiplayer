using Godot;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>Render the production effect variants before the player can enter a match.</summary>
public static class EffectWarmup
{
    private static readonly CancellationTokenSource StopPreparation=new();
    private static readonly HashSet<Action> PendingDraws=[];
    private sealed class DrawObservation(Node owner,Task frame,PreparationDrawBatch batch)
    {
        public readonly string Owner=owner.GetPath().ToString();
        public readonly long Started=System.Diagnostics.Stopwatch.GetTimestamp();
        public readonly ulong Process=Engine.GetProcessFrames();
        public readonly int Drawn=Engine.GetFramesDrawn();
        public readonly Task Frame=frame;
        public Task Delay=Task.CompletedTask;
        public readonly PreparationDrawBatch Batch=batch;
        public string Step="subscribed";
    }
    private static DrawObservation? _lastDraw;
    private static long _draws,_callbacks,_fallbacks,_returns;
    private static bool _starveDrawCallbacksForTest;
    private static long _starvedCallbacks;
    internal static long StarvedCallbacksForTest=>Interlocked.Read(ref _starvedCallbacks);
    internal static IDisposable StarveDrawCallbacksForTest()
    {
        if(!OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest=",StringComparison.Ordinal))||_starveDrawCallbacksForTest)
            throw new InvalidOperationException("Draw callback starvation is a scoped playtest-only probe.");
        _starveDrawCallbacksForTest=true;return new DrawStarvationScope();
    }
    private sealed class DrawStarvationScope:IDisposable
    {public void Dispose()=>_starveDrawCallbacksForTest=false;}

    public static object DrawDiagnostics=>new{
        begun=_draws,callbacks=_callbacks,fallbacks=_fallbacks,returned=_returns,pending=PendingDraws.Count,
        last=_lastDraw is {} last?new{owner=last.Owner,step=last.Step,
            ageMs=System.Diagnostics.Stopwatch.GetElapsedTime(last.Started).TotalMilliseconds,
            processFrames=Engine.GetProcessFrames()-last.Process,drawnFrames=Engine.GetFramesDrawn()-last.Drawn,
            frameTaskComplete=last.Frame.IsCompleted,delayTaskComplete=last.Delay.IsCompleted,
            batch=new{forceRendering=last.Batch.ForceRendering,normalWaits=last.Batch.NormalWaits,forcedDraws=last.Batch.ForcedDraws,completed=last.Batch.Completed}}:null};
    public static bool Stopping=>StopPreparation.IsCancellationRequested;
    public static int CancelPending()
    {
        int count=PendingDraws.Count;
        foreach(var callback in PendingDraws)RenderingServer.FramePostDraw-=callback;
        PendingDraws.Clear();StopPreparation.Cancel();
        return count;
    }
    public static async Task Draw(Node owner,PreparationDrawBatch batch)
    {
        StopPreparation.Token.ThrowIfCancellationRequested();
        var fence=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation=new DrawObservation(owner,fence.Task,batch);_lastDraw=observation;_draws++;
        void Completed()
        {
            Interlocked.Increment(ref _callbacks);
            if(_starveDrawCallbacksForTest){Interlocked.Increment(ref _starvedCallbacks);return;}
            fence.TrySetResult();
        }
        PendingDraws.Add(Completed);RenderingServer.FramePostDraw+=Completed;
        try
        {
            await batch.CompleteAsync(fence.Task,
                ()=>observation.Delay=Task.Delay(500,StopPreparation.Token),async ()=>
                {
                    _fallbacks++;observation.Step="force-draw";
                    StopPreparation.Token.ThrowIfCancellationRequested();
                    if(!GodotObject.IsInstanceValid(owner)||!owner.IsInsideTree())return;
                    // Keep a real scene-update boundary for newly selected previews
                    // and shutdown input, without waiting for a suppressed draw.
                    await ProcessFrame(owner.GetTree(),StopPreparation.Token);
                    StopPreparation.Token.ThrowIfCancellationRequested();
                    if(!GodotObject.IsInstanceValid(owner)||!owner.IsInsideTree())return;
                    RenderingServer.ForceDraw(false,0);
                    RenderingServer.ForceSync();
                    observation.Step="forced-draw-returned";
                },StopPreparation.Token);
        }
        finally
        {
            _returns++;observation.Step="returned";
            if(PendingDraws.Remove(Completed))RenderingServer.FramePostDraw-=Completed;
        }
    }
    private static async Task ProcessFrame(SceneTree tree,CancellationToken stopping)
    {
        var frame=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed()=>frame.TrySetResult();
        tree.ProcessFrame+=Completed;
        using var cancel=stopping.Register(()=>frame.TrySetCanceled(stopping));
        try {await frame.Task;}
        finally {if(GodotObject.IsInstanceValid(tree))tree.ProcessFrame-=Completed;}
    }

    public static async Task Run(Node owner,ArtMaterials art,Godot.Environment environment,DirectionalLight3D key,DirectionalLight3D fill,PreparationDrawBatch batch)
    {
        StopPreparation.Token.ThrowIfCancellationRequested();
        if(DisplayServer.GetName()=="headless")return;
        // Match the live format. Godot invalidates surface caches when MSAA
        // changes, so warm again after a preset change instead of mixing formats.
        var aa=GraphicsQuality.Antialiasing;
        var viewport=new SubViewport {Name="EffectWarmup",OwnWorld3D=true,Size=new(128,128),Msaa3D=aa,
            RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
        owner.AddChild(viewport);
        try
        {
            var world=new Node3D();viewport.AddChild(world);
            var look=(Godot.Environment)environment.Duplicate();
            world.AddChild(new WorldEnvironment {Environment=look});
            world.AddChild((DirectionalLight3D)key.Duplicate());world.AddChild((DirectionalLight3D)fill.Duplicate());
            var camera=new Camera3D {Position=new(0,12,19),Projection=Camera3D.ProjectionType.Orthogonal,Size=20,Current=true};
            world.AddChild(camera);camera.LookAt(Vector3.Zero);
            var effects=new List<Node3D>();
            void Add(Node3D model,Vector3 position)
            {model.Position=position;model.ProcessMode=Node.ProcessModeEnum.Disabled;world.AddChild(model);effects.Add(model);}
            foreach(var (mode,index) in new[]{("normal",0),("red",1),("remote",2),("bounce",3)})
            {
                var bomb=new BombModel(art);bomb.UpdateState(0,1,mode=="red",mode=="remote",2,null,1,mode);
                bomb._Process(.01);Add(bomb,new(-6+index*3,0,-4));
            }
            Add(new ExplosionView(art,1.4f,false,.08f),new(-4,0,0));
            Add(new ExplosionView(art,1.4f,true,.08f),new(4,0,0));
            Add(new ExplosionView(art,4f,false,.08f),new(0,0,-4));
            Add(new ExplosionView(art,4f,true,.08f),new(0,0,4));
            Add(new KickBurst(art,Vector3.Forward),new(-6,0,4));
            Add(new ExitEffect(art,true),new(-2,0,4));Add(new ExitEffect(art,false),new(2,0,4));
            // Zap flash first, then (after the advance below) flames on soot and the closing smoke puff.
            var flames=new BurnoutEffect(art,ArtMaterials.PlayerColors[0],0);Add(flames,new(-2,0,8));
            var smoke=new BurnoutEffect(art,ArtMaterials.PlayerColors[0],0);Add(smoke,new(2,0,8));
            var crush=new CrushEffect(art,ArtMaterials.PlayerColors[0],true);Add(crush,new(6,0,8));
            var dust=new CrushEffect(art,ArtMaterials.PlayerColors[0],false);Add(dust,new(-6,0,8));
            dust._Process(CrushEffect.PuffAt+.2f);
            var actor=new BomberModel(art,ArtMaterials.PlayerColors[0],"");actor.ShowName(false);actor.Pose(false,true,false);
            Add(actor,new(6,0,4));
            for(int i=0;i<6;i++)ArtGeometry.Box(world,new(-5+i*2,.4f,6),Vector3.One*(.12f+i*.008f),art.Color(new("d5b472")),.02f);
            for(int frame=0;frame<3;frame++)await Draw(owner,batch);
            // Geometry transparency activates another pipeline during the fade.
            foreach(var effect in effects)effect._Process(effect==flames?1:effect==smoke?1.8:effect is ExitEffect?.4:.2);
            actor.Pose(false,true,true);
            for(int frame=0;frame<3;frame++)await Draw(owner,batch);
            // Synchronize the small actual render, rather than assuming a preload
            // reference or a fixed sleep means the GPU used the pipeline.
            using var pixels=viewport.GetTexture().GetImage();
            if(pixels.IsEmpty())throw new InvalidOperationException("Effect warmup viewport did not render.");
        }
        finally
        {
            if(GodotObject.IsInstanceValid(owner)&&GodotObject.IsInstanceValid(viewport))
            {if(viewport.GetParent()==owner)owner.RemoveChild(viewport);viewport.QueueFree();}
        }
    }
}
