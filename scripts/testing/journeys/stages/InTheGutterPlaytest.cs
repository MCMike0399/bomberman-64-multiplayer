using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task InTheGutterTour()
    {
        Event("gutter-policy",new{ordinaryInputs=true,stateMutation=false,shallowWater="safe floor",current="authority-owned"});
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            _root.Navigate("Setup");await Frames(6);
            var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
            menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
            for(int n=0;n<6;n++)await KeyPress(Key.Right);
            Check(menu.Settings.Stage==BlastStages.InTheGutter,"native arena dial reaches In the Gutter");
            await Capture("in-the-gutter-menu-miniature");
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),4);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary Gutter battle starts",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            Check(terrain.Layout.Id==BlastStages.InTheGutter&&State.GetProperty("players").GetArrayLength()==4,"four original-relative post starts use accepted Gutter layout");
            Check(terrain.GetNodeOrNull<MeshInstance3D>("GutterShallowWater")!=null&&AuditDescendants(terrain).OfType<Node3D>().Any(n=>n.Name=="GutterCurrentMarker"),"shallow water and central flow marker render in the actual arena");
            Check(terrain.Scenery!=null&&!AuditDescendants(terrain.Scenery).OfType<CollisionObject3D>().Any(),"toy lock surroundings add no gameplay collision");
            var playable=new Rect2(-.35f,-.35f,terrain.Width+.7f,terrain.Depth+.7f);
            var intrusions=AuditDescendants(terrain.Scenery!).OfType<MeshInstance3D>().Where(n=>n.Mesh!=null).Where(n=>
            {
                var bounds=(terrain.GlobalTransform.AffineInverse()*n.GlobalTransform)*n.Mesh.GetAabb();
                return bounds.End.Y>=0&&playable.Intersects(new Rect2(bounds.Position.X,bounds.Position.Z,bounds.Size.X,bounds.Size.Z));
            }).Select(n=>n.Name.ToString()).ToArray();
            Check(intrusions.Length==0,"detailed waterworks stay outside the court's standing volume: "+string.Join(',',intrusions));
            await Capture("in-the-gutter-four-player-opening");
            if(!_headless)
            {
                foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
                {
                    await SelectGraphics(preset);CheckFraming(BlastStages.InTheGutter,GetWindow().Size);
                    var material=(ShaderMaterial)terrain.GetNode<MeshInstance3D>("GutterShallowWater").MaterialOverride;
                    Check(material.Shader==ArtMaterials.GutterWaterShaderFor(preset),"Gutter water follows the shared "+preset+" water identity");
                    await Capture("in-the-gutter-"+GraphicsQuality.ToWire(preset));
                }
                await SelectLocalLighting(StageTime.Night);await Capture("in-the-gutter-night");
                await SelectLocalLighting(StageTime.Midday);
                var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
                camera.Position=new(29,21,32);camera.LookAt(new Vector3(10.5f,.8f,10.5f));
                await Capture("in-the-gutter-oblique-inspection");camera.Transform=pose;
            }
            _root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},2,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"clear shallow-water exploration starts",180);
            Input(0,new PlayerInput(0,1));await Frames(42);Input(0,default);await Frames(8);
            Check(Player.GetProperty("alive").GetBoolean()&&Player.GetProperty("y").GetSingle()==0,"ordinary walking drops from the post into safe shallow water");
            await Capture("in-the-gutter-safe-shallow-court");
            Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
            Check(State.GetProperty("bombs").GetArrayLength()>0,"ordinary primary input places a live bomb in the current");
            await Capture("in-the-gutter-current-and-bomb");
            await KeyPress(Key.Escape);var paused=State.GetProperty("gutter").GetProperty("time").GetSingle();int snapshots=_root.SnapshotCount;
            await Frames(12);
            Check(_root.SnapshotCount==snapshots&&State.GetProperty("gutter").GetProperty("time").GetSingle()==paused,"pause freezes accepted current schedule");
            await ActivateNamed("ResumeGame");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:7,SuddenDeath:false),1);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player waterworks CPU battle starts",180);
            bool bomb=false,blast=false;
            for(int n=0;n<300&&_root.ScreenName=="Match";n++)
            {bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
            Check(bomb&&blast,"CPU players leave posts and create real bombs and explosions");
            await Capture("in-the-gutter-eight-player-battle");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,RoundSeconds:60,Ghosts:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"Gutter enters real sudden death",180);
            await WaitState(s=>s.GetProperty("closedLeft").GetInt32()>=3,"both grates enter the shallow court",180);
            Check(State.GetProperty("hurryMode").GetString()=="columns"&&State.GetProperty("closedLeft").GetInt32()==State.GetProperty("closedRight").GetInt32(),"closing grates track bilateral authoritative column hazard");
            Check(AuditDescendants(LightingArena).OfType<Node3D>().Any(n=>n.Name.ToString().StartsWith("CopperLockGatePanel")),"opaque metal grates replace generic red translucent walls");
            var westGate=AuditDescendants(LightingArena).OfType<Node3D>().Single(n=>n.Name=="CopperLockWestMachine").GetNode<Node3D>("CopperLockGatePanel");
            float gateX=westGate.Position.X;
            for(int frame=0;frame<12;frame++)
            {
                await Frames(1);Check(westGate.Position.X>=gateX-.001f,"accepted closure snapshots never pull the advancing gate backward");
                gateX=westGate.Position.X;
            }
            await Capture("in-the-gutter-closing-grates");
            await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean()),"grates crush exposed seats",240);
            Check(State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="columns"),"Gutter deaths retain the crush reason");
            await Capture("in-the-gutter-grate-crush");
        }
        finally
        {
            Input(0,default);Input(1,default);_root.Navigate("Modes");await Frames(3);
            if(!_headless){await SelectGraphics(quality);await SelectLocalLighting(lighting);}
        }
    }

    private async Task InTheGutterNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Waterworks host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Waterworks observer",settings,NetworkMode.JoinCode,host.JoinCode);
            Action pump=()=>peer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20)
                {await Frames(1);await Task.Delay(16);}
                Check(host.Party?.CanStart==true,mode+" admits two compatible Gutter clients");host.RequestStart();
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts real Gutter authority",900);
                Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.InTheGutter,mode+" accepts the correct stage");
                await MoveTo(4.5f,9.5f,240);
                Check(Player.GetProperty("y").GetSingle()==0,mode+" accepted ordinary movement reaches the shallow floor");
                Input(0,new PlayerInput(0,0,A:true));
                await WaitState(s=>s.GetProperty("bombs").GetArrayLength()>0,mode+" accepts a real ground bomb",120);
                Input(0,default);
                bool stageSeen=false,currentSeen=false,bombSeen=false;clock.Restart();
                while(clock.Elapsed.TotalSeconds<8&&!(stageSeen&&currentSeen&&bombSeen))
                {
                    await Frames(1);await Task.Delay(16);
                    foreach(var frame in peer.DrainMessages())
                    {
                        string? kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                        if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.InTheGutter;
                        if(kind!="state")continue;
                        currentSeen|=data.GetProperty("gutter").ValueKind==System.Text.Json.JsonValueKind.Object;
                        bombSeen|=data.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==host.PlayerId&&b.GetProperty("y").GetSingle()==0);
                    }
                }
                Event("gutter-network-observer",new{mode,stageSeen,currentSeen,bombSeen});
                Check(stageSeen,mode+" observer receives the real Gutter stage");
                Check(currentSeen,mode+" observer receives the accepted current schedule");
                Check(bombSeen,mode+" observer receives the real ground bomb");
                await Capture("in-the-gutter-network-"+mode.ToString().ToLowerInvariant());
            }
            finally
            {
                Input(0,default);GetTree().ProcessFrame-=pump;_root.Back();await Frames(3);
            }
        }
    }
    private async Task CopperLockShapes()
    {
        Event("copper-lock-shape-policy",new{authorityUnchanged=true,coreMachinesOnLow=true,materials="blocked palette; detail review pending"});
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Copper Lock shape study enters actual eight-player battle",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var core=AuditDescendants(terrain).OfType<Node3D>().Where(n=>n.Name.ToString().StartsWith("CopperLock")).ToArray();
            Check(core.Count(n=>n.Name.ToString().StartsWith("CopperLockFatGateBar"))==12,"two portcullises have six chunky bars each");
            Check(core.Count(n=>n.Name.ToString().StartsWith("CopperLockDriveGear"))==2,"both core sluice machines have one large drive gear");
            Check(core.Count(n=>n.Name.ToString().StartsWith("CopperLockBollardCap"))==4,"four original-relative posts have rounded bollard caps");
            Check(!AuditDescendants(terrain).Any(n=>n.Name=="GutterFlowTracers"||n.Name=="GutterLockHouse"),"scanline traces and cloned houses are removed");
            foreach(var node in core.OfType<MeshInstance3D>().Where(n=>n.Name.ToString().StartsWith("CopperLockRing")||
                n.Name.ToString().StartsWith("CopperLockBowedGantry")||n.Name=="CopperLockCulvertArch"||n.Name=="CopperLockSolidGearDisc"||n.Name=="CopperLockHalfRoundCoping"||n.Name=="CopperLockCurvedWeirWater"))
            {
                bool clockwise=true;int triangles=0;
                for(int surface=0;surface<node.Mesh.GetSurfaceCount();surface++)
                {
                    var arrays=node.Mesh.SurfaceGetArrays(surface);
                    var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var indices=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?
                        Enumerable.Range(0,vertices.Length).ToArray():arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        int a=indices[i],b=indices[i+1],c=indices[i+2];
                        var cross=(vertices[b]-vertices[a]).Cross(vertices[c]-vertices[a]);
                        clockwise&=cross.LengthSquared()>1e-10f&&cross.Dot(normals[a]+normals[b]+normals[c])<0;triangles++;
                    }
                }
                Check(clockwise&&triangles>0,"actual indexed Copper Lock faces are nondegenerate and clockwise against their outward normals: "+node.Name);
            }
            if(!_headless)
            {
                foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.High})
                {
                    await SelectGraphics(preset);
                    Check(core.Where(n=>n.Name=="CopperLockWestMachine"||n.Name=="CopperLockEastMachine").All(n=>n.IsVisibleInTree()),
                        "arena body and both hero machines remain visible at "+preset);
                    Check(preset!=GraphicsPreset.Low||terrain.Scenery?.IsVisibleInTree()!=true,"Low hides exterior scenery while preserving core architecture");
                    Check(terrain.GetNode<MeshInstance3D>("CopperLockOuterWater").IsVisibleInTree(),"core outer water remains visible at "+preset);
                    await Capture("copper-lock-shapes-"+GraphicsQuality.ToWire(preset));
                }
                var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
                camera.Position=new(31,22,32);camera.LookAt(new Vector3(10.5f,1.2f,10.5f));
                await Capture("copper-lock-shapes-oblique");camera.Transform=pose;
                await SelectLocalLighting(StageTime.Night);await Capture("copper-lock-shapes-night");
            }
            else Check(core.Any(n=>n.Name=="CopperLockWestMachine")&&core.Any(n=>n.Name=="CopperLockEastMachine"),
                "headless shape scene retains both actual hero machines");
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            if(!_headless){await SelectGraphics(quality);await SelectLocalLighting(lighting);}
        }
    }

    private async Task GutterWaterContact()
    {
        Check(!_headless,"shallow water contact regression requires actual rendered pixels");
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        var size=GetWindow().Size;
        try
        {
            await ResizeWindow(new(1280,800));await SelectGraphics(GraphicsPreset.High);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),4);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"water-contact regression enters actual Gutter battle",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            CheckCopperWaterInputs(terrain);
            var water=(ShaderMaterial)terrain.GetNode<MeshInstance3D>("GutterShallowWater").MaterialOverride;
            var tracer=terrain.GutterWakeMaterial!;
            Check(water.Shader==ArtMaterials.GutterWaterShaderFor(GraphicsPreset.High),"regression observes the actual specialized High surface");
            Check(water.Shader.Code.Contains("#define SCENE_REFLECTIONS",StringComparison.Ordinal),"Gutter High retains the full scene-reflection branch");
            Check(ArtMaterials.WaterShaderFor(GraphicsPreset.High).Code.Contains("float jitter=fract(",StringComparison.Ordinal),"other stages retain their established jittered High reflection");
            var shader=water.Shader;string original=shader.Code,originalTracer=tracer.Shader.Code;
            string clocked=original.Replace("TIME","gutter_test_clock",StringComparison.Ordinal)
                .Replace("uniform float flood_amount=0.0;","uniform float gutter_test_clock=4.0;\nuniform float flood_amount=0.0;",StringComparison.Ordinal);
            var defects=new List<string>();
            try
            {
                // Pin only cosmetic water/tracer clocks. Real characters, opaque
                // scene inputs, shadows, terrain, cover and authority remain active.
                tracer.Shader.Code=originalTracer.Replace("current_clock*","4.0*",StringComparison.Ordinal);
                foreach(var time in new[]{StageTime.Midday,StageTime.Night})
                {
                    await SelectLocalLighting(time);shader.Code=clocked;await Frames(6);
                    await Capture("gutter-contact-"+time.ToString().ToLowerInvariant()+"-high",0);
                    string reflectedPath=_screenshots[^1];
                    using var reflected=Image.LoadFromFile(reflectedPath);
                    int reflectedNoise=GutterContactSpeckles(reflected);
                    shader.Code=clocked.Replace("#define SCENE_REFLECTIONS","",StringComparison.Ordinal);await Frames(6);
                    await Capture("gutter-contact-"+time.ToString().ToLowerInvariant()+"-reflection-control",0);
                    using var control=Image.LoadFromFile(_screenshots[^1]);
                    int controlNoise=GutterContactSpeckles(control),mirrored=0;
                    for(int y=(int)(reflected.GetHeight()*.24f);y<(int)(reflected.GetHeight()*.84f);y++)
                    for(int x=(int)(reflected.GetWidth()*.19f);x<(int)(reflected.GetWidth()*.81f);x++)
                    {
                        var delta=reflected.GetPixel(x,y)-control.GetPixel(x,y);
                        if(Math.Max(Math.Abs(delta.R),Math.Max(Math.Abs(delta.G),Math.Abs(delta.B)))>.04f)mirrored++;
                    }
                    Event("gutter-water-contact",new{lighting=time.ToString(),reflectedNoise,controlNoise,mirrored});
                    if(reflectedNoise>controlNoise+150)defects.Add(time+" reflected contacts have isolated fringe pixels: "+reflectedNoise+" vs "+controlNoise);
                    if(mirrored<250)defects.Add(time+" High lost actual opaque-scene reflections: "+mirrored);
                    shader.Code=clocked;
                }
            }
            finally{shader.Code=original;tracer.Shader.Code=originalTracer;}
            Check(defects.Count==0,"shallow contacts stay smooth while High retains actual post and cover reflections"+
                (defects.Count==0?"":": "+string.Join("; ",defects)));
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            await SelectGraphics(quality);await SelectLocalLighting(lighting);await ResizeWindow(size);
        }
    }

    private static int GutterContactSpeckles(Image image)
    {
        int count=0,width=image.GetWidth(),height=image.GetHeight();
        float Bright(Color c)=>(c.R+c.G+c.B)/3;
        for(int y=(int)(height*.24f);y<(int)(height*.84f);y++)
        for(int x=(int)(width*.19f);x<(int)(width*.81f);x++)
        {
            var c=image.GetPixel(x,y);
            if(c.G<=c.R*1.05f||c.B<=c.R*1.05f||c.G<=80f/255)continue;
            float value=Bright(c);
            float[] neighbors=[Bright(image.GetPixel(x+1,y)),Bright(image.GetPixel(x-1,y)),
                Bright(image.GetPixel(x,y+1)),Bright(image.GetPixel(x,y-1))];
            if(neighbors.Count(v=>value-v>13f/255)>=3&&value-neighbors.Average()>18f/255)count++;
        }
        return count;
    }

}
