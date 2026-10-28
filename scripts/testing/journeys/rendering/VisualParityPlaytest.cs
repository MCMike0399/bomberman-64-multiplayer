using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Bomber.Game.Online;
using System.Text.RegularExpressions;
using Bomber.Game.Matches;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // First full-scene measurement packet. Observations remain unqualified until
    // repeat/control and complete inventory/history checks have been reviewed.
    private static string VisualDescriptionHash(object value)
    {
        using var bytes=new MemoryStream();using(var writer=new Utf8JsonWriter(bytes))ReplayCanonical(writer,JsonSerializer.SerializeToElement(value));
        return ReplayHash(bytes.ToArray());
    }
    private async Task VisualParity()
    {
        Check(!_headless,"full-quality visual parity requires an actual renderer");
        var original=GraphicsQuality.Current;var priorRootProcess=_root.IsProcessing();
        var previousViews=AuditDescendants(_root).OfType<SubViewport>().ToDictionary(v=>v,v=>v.RenderTargetUpdateMode);
        var existingArena=LightingArena;bool arenaVisible=existingArena.Visible;
        var records=new List<object>();
        try
        {
            await WindowMode(Window.ModeEnum.Windowed,"visual fixture owns a windowed QA surface");
            await ResizeWindow(new(1280,800));
            foreach(var view in previousViews.Keys)view.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;
            _root.SetProcess(false);existingArena.Visible=false;
            GraphicsQuality.Apply(GraphicsPreset.High,GetViewport());
            foreach(string trial in new[]{"warmup","base-a","base-b","dof-off","reflection-off"})
                await Trial(trial);
        }
        finally
        {
            existingArena.Visible=arenaVisible;_root.SetProcess(priorRootProcess);
            foreach(var (view,mode) in previousViews)if(GodotObject.IsInstanceValid(view))view.RenderTargetUpdateMode=mode;
            GraphicsQuality.Apply(original,GetViewport());
        }
        File.WriteAllText(Path.Combine(_output,"visual-parity-observations.json"),JsonSerializer.Serialize(new{
            schemaVersion=1,qualification="Measured inputs; package manifests only when archive/module/executable provenance binds; external repeat/control validation required",
            limitations=new[]{"Renderer globals described by deterministic ApplyHigh policy because stock getters are unavailable","Target API/OS provenance is recorded; physical target GPU/performance scope remains separate","No arbitrary-driver or physical-display equality claim"},
            driver=RenderingServer.GetCurrentRenderingDriverName(),adapter=RenderingServer.GetVideoAdapterName(),
            deviceType=RenderingServer.GetVideoAdapterType().ToString(),engine=Engine.GetVersionInfo()["string"].AsString(),records
        },new JsonSerializerOptions{WriteIndented=true}));

        async Task Trial(string label)
        {
            var viewport=new SubViewport{Name="FullHighFixture",Size=new(1280,800),OwnWorld3D=true,TransparentBg=false,
                RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};
            GraphicsQuality.ApplyViewport(viewport);viewport.Scaling3DScale=1;AddChild(viewport);
            var arena=new ArenaView{Name="ProductionArena"};viewport.AddChild(arena);
            var overlay=new CanvasLayer{Layer=200};AddChild(overlay);
            var displayed=new TextureRect{Texture=viewport.GetTexture(),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};
            overlay.AddChild(displayed);displayed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var shaders=new Dictionary<Shader,string>();
            var draws=new List<object>();
            try
            {
                var match=new LocalMatch(new MatchOptions{Stage="up-and-down",HumanPlayers=1,BotPlayers=0,Practice=true,Seed=64});
                arena.Stage(match.StageMessage!.Value.GetProperty("d"));arena.SetBattleActive(true);
                arena.SetTimeOfDay(StageTime.Night);
                void FreezeProcessing()
                {
                    foreach(var node in AuditDescendants(arena).Prepend(arena))
                    {node.SetProcess(false);node.SetPhysicsProcess(false);}
                }
                FreezeProcessing();long snapshot=-1;
                while(match.TickNumber<120)
                {
                    match.Tick();var state=match.StateMessage!.Value;long tick=state.GetProperty("k").GetInt64();
                    if(tick!=snapshot){arena.State(state.GetProperty("d"),tick);snapshot=tick;}
                    FreezeProcessing();
                    foreach(var node in AuditDescendants(arena).Prepend(arena).ToArray())
                    {
                        var method=node.GetType().GetMethod("_Process",BindingFlags.Instance|BindingFlags.Public);
                        if(method?.DeclaringType?.Namespace?.StartsWith("BlastCircuit.Arena",StringComparison.Ordinal)==true)node._Process(1d/30);
                    }
                }
                FreezeProcessing();
                var materials=VisualMaterials(arena).OfType<ShaderMaterial>().Distinct().ToArray();
                foreach(var material in materials)
                {
                    var shader=material.Shader;if(shaders.ContainsKey(shader))continue;
                    shaders[shader]=shader.Code;
                    if(Regex.IsMatch(shader.Code,@"\bTIME\b"))
                        shader.Code=Regex.Replace(shader.Code,@"\bTIME\b","parity_fixture_time").Replace("shader_type spatial;","shader_type spatial;\nuniform float parity_fixture_time=4.0;",StringComparison.Ordinal);
                }
                var camera=AuditDescendants(arena).OfType<Camera3D>().Single();
                var attributes=(CameraAttributesPractical)camera.Attributes;
                var environment=AuditDescendants(arena).OfType<WorldEnvironment>().Single().Environment;
                Check(viewport.Msaa3D==Viewport.Msaa.Msaa2X&&GraphicsQuality.Current==GraphicsPreset.High&&environment.SsilEnabled&&
                    attributes.DofBlurFarEnabled&&attributes.DofBlurNearEnabled,"fresh fixture retains production High, 2xMSAA, SSIL and near/far DOF: "+label);
                if(label=="dof-off")attributes.DofBlurNearEnabled=attributes.DofBlurFarEnabled=false;
                if(label=="reflection-off")foreach(var shader in shaders.Keys)
                    if(shader.Code.Contains("#define SCENE_REFLECTIONS",StringComparison.Ordinal))shader.Code=shader.Code.Replace("#define SCENE_REFLECTIONS","",StringComparison.Ordinal);
                for(int ordinal=1;ordinal<=16;ordinal++)await Draw(ordinal);
                if(label!="warmup")
                {
                    // Both readbacks use the same disabled target; no extra draw.
                    for(int readback=0;readback<(label=="base-a"?2:1);readback++)
                    {
                        using var image=viewport.GetTexture().GetImage();
                        if(image.IsEmpty()||image.GetWidth()!=1280||image.GetHeight()!=800)throw new IOException("Invalid full High readback");
                        string path=Path.Combine(_output,$"parity-{label}-{readback}.png");
                        if(image.SavePng(path)!=Error.Ok)throw new IOException("Could not save full High fixture");
                        _screenshots.Add(path);
                    }
                }
                var inventory=VisualResourceInventory(arena,label);
                var inventoryIdentity=JsonSerializer.SerializeToNode(inventory)!.AsObject();inventoryIdentity.Remove("binary");
                string assetsSha256=VisualDescriptionHash(inventoryIdentity);
                object Vector(Vector3 value)=>new[]{value.X,value.Y,value.Z};
                var transform=camera.GlobalTransform;
                var cameraDescription=new{origin=Vector(transform.Origin),x=Vector(transform.Basis.X),y=Vector(transform.Basis.Y),z=Vector(transform.Basis.Z),
                    camera.Fov,camera.Near,camera.Far,camera.HOffset,camera.VOffset};
                var settingsDescription=new{
                    viewport=new{size=new[]{viewport.Size.X,viewport.Size.Y},msaa=viewport.Msaa3D.ToString(),viewport.UseTaa,viewport.UseDebanding,
                        viewport.UseHdr2D,aa=viewport.ScreenSpaceAA.ToString(),viewport.Scaling3DScale,scaling=viewport.Scaling3DMode.ToString(),viewport.TransparentBg},
                    policy=new{preset=GraphicsQuality.Current.ToString(),GraphicsQuality.ShaderDetail,GraphicsQuality.ShaderPremium,
                        shadowAtlas=4096,shadowFilter="SoftMedium",ssao="Medium/half",ssil="Low/half",dof="Low/Circle/jitter-flag-on",
                        source="Unmodified GraphicsQuality.Apply(High) called before this recipe; global settings have no stock getters"},
                    environmentAndCameraStorage=inventoryIdentity};
                string settingsSha256=VisualDescriptionHash(settingsDescription);
                var conditions=new{sceneId="full-up-down-high-v1",assetsSha256,stateId=VisualDescriptionHash(new{authority=match.StateMessage,inventory=inventoryIdentity}),
                    tick=match.TickNumber,width=1280,height=800,
                    render=new{preset="High",msaaSamples=2,dof=attributes.DofBlurNearEnabled&&attributes.DofBlurFarEnabled,settingsSha256},
                    cameraSha256=VisualDescriptionHash(cameraDescription),shaderTimeSeconds=4d,animationTimeSeconds=118d/30,
                    temporal=new{historyId="new-OwnWorld3D-SubViewport/renderbuffers;fixed-118-steps;16-zero-step-explicit-draws-v1",warmupFrames=16,
                        jitterId="Circle/Low/jitter-flag-on;tagged-Circle-ignores-seed;no-reseed;seed-unobserved"},
                    capture=new{surface="SubViewport-LDR-post-Filmic/raw-RGBA8-or-RGB8;transformflush/sync/once/draw0/sync/readback/disable;no-fallback",
                        colorContract="Raw8bit-LDR-post-production-Filmic;stored-alpha;no-extra-conversion-v1"}};
                var recipe=new{stage="up-and-down",seed=64,tick=match.TickNumber,presentationSteps=118,stepSeconds=1d/30,
                    shaderTimeSeconds=4,lighting="Night",preset="High",msaaSamples=2,scale=viewport.Scaling3DScale,
                    ssil=environment.SsilEnabled,dofNear=attributes.DofBlurNearEnabled,dofFar=attributes.DofBlurFarEnabled,
                    dofAmount=attributes.DofBlurAmount,dofNearDistance=attributes.DofBlurNearDistance,dofFarDistance=attributes.DofBlurFarDistance,
                    camera=cameraDescription,settings=settingsDescription,conditions,
                    geometry=TriangleStreamFingerprint(arena),resourceInventory=inventory,authority=match.StateMessage,
                    shaders=shaders.Select(pair=>new{original=ReplayHash(Encoding.UTF8.GetBytes(pair.Value)),active=ReplayHash(Encoding.UTF8.GetBytes(pair.Key.Code))}).ToArray(),
                    temporal=new{reset="New SubViewport/OwnWorld3D/ArenaView and render buffers per trial",warmupDraws=16,
                        jitter="Production Circle/Low, flag on; tagged Circle does not consume seed; no reseed",draws},
                    capture="SubViewport post-tonemap GetImage; flush Node3D transforms/sync/once/zero-step draw/sync/readback/disable; no unplanned target or fallback draws"};
                records.Add(new{label,recipe});
                File.WriteAllText(Path.Combine(_output,$"parity-{label}-recipe.json"),JsonSerializer.Serialize(recipe,new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(Path.Combine(_output,$"parity-{label}-conditions.json"),JsonSerializer.Serialize(conditions,new JsonSerializerOptions{WriteIndented=true}));
                string? provenancePath=OS.GetCmdlineUserArgs().FirstOrDefault(arg=>arg.StartsWith("--parity-provenance=",StringComparison.Ordinal))?.Split('=',2)[1];
                if(provenancePath is not null&&label is "base-a" or "base-b")
                {
                    using var supplied=JsonDocument.Parse(File.ReadAllText(provenancePath));var suppliedRoot=supplied.RootElement;
                    if(suppliedRoot.GetProperty("sourceRevision").GetString()!=OnlineProtocol.SourceRevision||
                        suppliedRoot.GetProperty("entryAssemblySha256").GetString()!=ReplayAssemblyHash(typeof(PlaytestDriver).Assembly)||
                        suppliedRoot.GetProperty("templateSha256").GetString()!=ReplayHash(File.ReadAllBytes(OS.GetExecutablePath())))
                        throw new InvalidDataException("Visual fixture provenance does not bind the executing package module/source");
                    if(inventoryIdentity["unsupported"]!.AsArray().Count!=0)throw new InvalidDataException("Visual resource inventory contains unbound inputs");
                    foreach(string hash in new[]{suppliedRoot.GetProperty("packageSha256").GetString()!,suppliedRoot.GetProperty("templateSha256").GetString()!})
                        if(!Regex.IsMatch(hash,"^[0-9a-f]{64}$"))throw new InvalidDataException("Visual package/template provenance is invalid");
                    var provenance=new{platform=OS.GetName()+":"+OS.GetVersion()+":"+RuntimeInformation.ProcessArchitecture,
                        api=RenderingServer.GetCurrentRenderingDriverName(),device=RenderingServer.GetVideoAdapterName()+":"+RenderingServer.GetVideoAdapterType(),
                        driver="API:"+RenderingServer.GetVideoAdapterApiVersion()+";OS:"+OS.GetVersion(),engineIdentity=Engine.GetVersionInfo()["string"].AsString()+":"+Engine.GetVersionInfo()["hash"].AsString(),
                        templateSha256=suppliedRoot.GetProperty("templateSha256").GetString(),packageSha256=suppliedRoot.GetProperty("packageSha256").GetString(),
                        sourceIdentity="git:"+OnlineProtocol.SourceRevision,runtimeIdentity=RuntimeInformation.FrameworkDescription+":"+RuntimeInformation.ProcessArchitecture};
                    for(int readback=0;readback<(label=="base-a"?2:1);readback++)
                    {
                        string png=Path.Combine(_output,$"parity-{label}-{readback}.png");
                        File.WriteAllText(Path.ChangeExtension(png,".manifest.json"),JsonSerializer.Serialize(new{schemaVersion=1,pngSha256=ReplayHash(File.ReadAllBytes(png)),conditions,provenance},new JsonSerializerOptions{WriteIndented=true}));
                    }
                }
            }
            finally
            {
                viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;
                foreach(var (shader,code) in shaders)shader.Code=code;
                overlay.Free();viewport.Free();
            }

            Task Draw(int ordinal)
            {
                if(OS.GetThreadCallerId()!=OS.GetMainThreadId())throw new InvalidOperationException("Fixture draw/readback must stay on main thread");
                if(viewport.RenderTargetUpdateMode!=SubViewport.UpdateMode.Disabled)throw new InvalidOperationException("Unaccounted fixture draw request");
                int pre=0;void Pre(){pre++;}
                RenderingServer.FramePreDraw+=Pre;
                try
                {
                    foreach(var spatial in AuditDescendants(arena).Prepend(arena).OfType<Node3D>())spatial.ForceUpdateTransform();
                    RenderingServer.ForceSync();
                    ulong process=Engine.GetProcessFrames();int drawn=Engine.GetFramesDrawn();
                    viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
                    RenderingServer.ForceDraw(false,0);RenderingServer.ForceSync();
                    using var completed=viewport.GetTexture().GetImage();
                    viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;
                    if(completed.IsEmpty()||pre!=1)throw new InvalidOperationException("Planned full High draw/readback did not complete exactly once");
                    draws.Add(new{ordinal,processBefore=process,drawnBefore=drawn,processAfter=Engine.GetProcessFrames(),drawnAfter=Engine.GetFramesDrawn(),
                        manualDraws=1,frameStep=0,fallbackDraws=0,preSignals=pre,readbackBytes=completed.GetData().Length});
                }
                finally{viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled;RenderingServer.FramePreDraw-=Pre;}
                return Task.CompletedTask;
            }
        }
    }
}
