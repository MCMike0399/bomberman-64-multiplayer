using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task TideglassCoast()
    {
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.UpAndDown,Bots:7,SuddenDeath:false,RoundSeconds:300),1,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"coast witness enters real eight-player Up & Down",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var islet=terrain.GetNode<Node3D>("LighthouseIsletCore");var arch=terrain.GetNode<Node3D>("TideglassSeaArch");var bell=terrain.GetNode<Node3D>("TideglassBellBuoy");
            var archMesh=arch.GetNode<MeshInstance3D>("CarvedCoastalArch");
            var bellMesh=bell.GetNode<MeshInstance3D>("CoastalBell");
            foreach(var node in new[]{archMesh,bellMesh}.Concat(AuditDescendants(terrain.Scenery!).OfType<MeshInstance3D>().Where(n=>n.Name=="ErodedSeaStack")))
            {
                var a=node.Mesh.SurfaceGetArrays(0);var v=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();var indices=a[(int)Mesh.ArrayType.Index].AsInt32Array();bool front=true;
                for(int i=0;i<indices.Length;i+=3){int x=indices[i],y=indices[i+1],z=indices[i+2];var cross=(v[y]-v[x]).Cross(v[z]-v[x]);front&=cross.LengthSquared()>1e-10f&&cross.Dot(n[x]+n[y]+n[z])<0;}
                Check(front&&v.All(p=>p.IsFinite())&&n.All(p=>p.IsFinite()),"coastal arch, bell and smoothed stack triangles are finite and clockwise: "+node.Name);
            }
            Check(CoastalMeshHits(archMesh,arch.ToGlobal(new(0,1.05f,-2)),arch.ToGlobal(new(0,1.05f,2))).Count==0,
                "captured sea arch has an actual opening through the former stack");
            var hero=new List<Vector3>();
            foreach(var root in new[]{islet,arch,bell})foreach(var mesh in AuditDescendants(root).OfType<MeshInstance3D>().Where(n=>!n.Name.ToString().StartsWith("LighthouseSoftBeam")))
            for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                hero.AddRange(mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(p=>mesh.ToGlobal(p)));
            Check(hero.All(p=>p.X<2.65f||p.X>21.35f),"core coastal landmarks stay outside all standing routes and aim space");
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
            {
                var viewport=new SubViewport{Size=size,Disable3D=true};AddChild(viewport);
                try
                {
                    var camera=new Camera3D{Fov=36,Near=.1f,Far=180};viewport.AddChild(camera);new BattleCameraRig(camera).SetStage(_root.LatestStage!.Value);
                    Vector2 min=Vector2.One,max=Vector2.Zero;foreach(var point in hero){var p=camera.UnprojectPosition(point)/new Vector2(size.X,size.Y);min=min.Min(p);max=max.Max(p);}
                    Event("tideglass-core-framing",new{size=size.ToString(),minimum=min.ToString(),maximum=max.ToString(),topWorld=hero.OrderBy(p=>camera.UnprojectPosition(p).Y).First().ToString(),vertices=hero.Count});
                    Check(min.X>=.02f&&max.X<=.98f&&min.Y>=.14f&&max.Y<=.9f,"core lighthouse, arch and buoy remain framed by the ordinary "+size+" camera");
                }
                finally{viewport.Free();}
            }
            foreach(var preset in new[]{GraphicsPreset.High,GraphicsPreset.Medium,GraphicsPreset.Low})
            {
                await SelectGraphics(preset);Check(islet.IsVisibleInTree()&&arch.IsVisibleInTree()&&bell.IsVisibleInTree(),"lighthouse footing, arch and bell buoy survive "+preset);
                Check(islet.GetNode<Node3D>("Lighthouse/LighthouseBeam").IsVisibleInTree()==(preset!=GraphicsPreset.Low),"Medium/High retain soft beam volume while Low keeps its emissive lens");
                await Capture("tideglass-"+GraphicsQuality.ToWire(preset)+"-midday");
            }
            await SelectLocalLighting(StageTime.Night);await Capture("tideglass-low-night");
            var lens=(StandardMaterial3D)islet.GetNode<MeshInstance3D>("Lighthouse/LighthouseLens").MaterialOverride;
            float minEnergy=float.MaxValue,maxEnergy=0,lowY=bell.Position.Y,highY=lowY;
            for(int i=0;i<40;i++){await Frames(1);minEnergy=Math.Min(minEnergy,lens.EmissionEnergyMultiplier);maxEnergy=Math.Max(maxEnergy,lens.EmissionEnergyMultiplier);lowY=Math.Min(lowY,bell.Position.Y);highY=Math.Max(highY,bell.Position.Y);}
            Check(maxEnergy-minEnergy>.01f&&highY-lowY>.005f,"Low retains the shared slow beacon and floating-bell life");
            Check(Math.Abs(bell.Position.Y-terrain.WaterHeight)<=.061f,"bell buoy rides the actual rendered accepted-water plane");
            await KeyPress(Key.Escape);var paused=bell.Transform;float pausedLens=lens.EmissionEnergyMultiplier;await Frames(12);
            Check(bell.Transform.IsEqualApprox(paused)&&lens.EmissionEnergyMultiplier==pausedLens,"core coastal clock freezes on ordinary local pause");await ActivateNamed("ResumeGame");
            await SelectGraphics(GraphicsPreset.High);await Frames(90);await Capture("tideglass-high-night-combat");await SelectLocalLighting(StageTime.Midday);
            var actual=_root.GetViewport().GetCamera3D()!;var inspect=new Camera3D{Fov=36,Near=.1f,Far=180,Current=true};AddChild(inspect);
            try
            {
                inspect.Position=new(terrain.Width-3,9,14);inspect.LookAt(arch.GlobalPosition+new Vector3(0,2.1f,0));inspect.MakeCurrent();await Frames(3);
                var viewport=_root.GetViewport().GetVisibleRect().Size;var points=archMesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(p=>archMesh.ToGlobal(p));
                Vector2 min=Vector2.One,max=Vector2.Zero;bool behind=false;
                foreach(var point in points){var p=inspect.UnprojectPosition(point)/viewport;min=min.Min(p);max=max.Max(p);behind|=inspect.IsPositionBehind(point);}
                Event("tideglass-arch-inspection-framing",new{minimum=min.ToString(),maximum=max.ToString(),camera=inspect.Position.ToString()});
                Check(_root.ScreenName=="Match"&&terrain.IsVisibleInTree()&&inspect.IsCurrent()&&!behind&&min.X>=.03f&&max.X<=.97f&&min.Y>=.14f&&max.Y<=.9f,
                    "temporary coastal inspection really frames the visible arch below the HUD");
                await Capture("tideglass-arch-inspection");
            }
            finally{inspect.Free();actual.MakeCurrent();}
        }
        finally{Input(0,default);_root.Navigate("Modes");await Frames(3);await SelectGraphics(original);await SelectLocalLighting(lighting);}
    }

    private static List<Vector3> CoastalMeshHits(MeshInstance3D node,Vector3 from,Vector3 to)
    {
        var hits=new List<Vector3>();var direction=to-from;var transform=node.GlobalTransform;
        var arrays=node.Mesh.SurfaceGetArrays(0);var v=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        for(int i=0;i<ids.Length;i+=3)
        {
            var a=transform*v[ids[i]];var b=transform*v[ids[i+1]];var c=transform*v[ids[i+2]];var e1=b-a;var e2=c-a;var h=direction.Cross(e2);
            float d=e1.Dot(h);if(Math.Abs(d)<1e-7f)continue;var offset=from-a;float u=offset.Dot(h)/d;if(u<0||u>1)continue;
            var q=offset.Cross(e1);float w=direction.Dot(q)/d;if(w<0||u+w>1)continue;float t=e2.Dot(q)/d;if(t>=0&&t<=1)hits.Add(from+direction*t);
        }
        return hits;
    }
}
