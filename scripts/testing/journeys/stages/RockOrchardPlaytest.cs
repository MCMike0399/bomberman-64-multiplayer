using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task RockOrchard()
    {
        var preset=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.RockGarden,Bots:7,SuddenDeath:false,RoundSeconds:300),1,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"orchard review enters real eight-player combat",180);
            var arena=LightingArena;var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            VerifyGardenGeometry(arena);
            var mill=terrain.GetNode<Node3D>("Windmill");var branch=terrain.GetNode<Node3D>("OrchardFruitBranch");
            var landmarks=new List<Vector3>();
            foreach(var root in new[]{mill,branch})foreach(var mesh in AuditDescendants(root).OfType<MeshInstance3D>())
                for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                    foreach(var point in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())landmarks.Add(mesh.ToGlobal(point));
            Check(landmarks.All(p=>p.Z<.65f||p.X<-.35f),"core mill and branch remain outside standing and aiming space");
            var front=terrain.GetChildren().OfType<MeshInstance3D>().Where(n=>n.Position.Z>terrain.Depth-1&&n.Position.Z<terrain.Depth&&
                n.Mesh.GetAabb().Size.Y>.5f&&n.Position.Y>=0).ToArray();
            var frontPoints=front.SelectMany(n=>OrchardAabbCorners(n.Mesh.GetAabb()).Select(p=>n.ToGlobal(p))).ToList();
            var cap=terrain.GetNode<MeshInstance3D>("GardenCreamCopingSouth");frontPoints.AddRange(OrchardAabbCorners(cap.Mesh.GetAabb()).Select(p=>cap.ToGlobal(p)));
            Check(front.Length==terrain.Width,"framing audit consumes every actual front-wall segment");
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
            {
                var view=new SubViewport{Size=size,Disable3D=true};AddChild(view);
                try
                {
                    var camera=new Camera3D{Fov=36,Near=.1f,Far=180};view.AddChild(camera);new BattleCameraRig(camera).SetStage(_root.LatestStage!.Value);
                    var denominator=new Vector2(size.X,size.Y);
                    float bottom=frontPoints.Max(p=>camera.UnprojectPosition(p).Y/size.Y);
                    Vector2 min=Vector2.One,max=Vector2.Zero;foreach(var point in landmarks){var at=camera.UnprojectPosition(point)/denominator;min=min.Min(at);max=max.Max(at);}
                    Event("orchard-standard-framing",new{size=size.ToString(),frontWallBottom=bottom,footerStarts=.925f,
                        footerGapPixels=(.925f-bottom)*size.Y,topWorld=landmarks.OrderBy(p=>camera.UnprojectPosition(p).Y).First().ToString(),landmarkMinimum=min.ToString(),landmarkMaximum=max.ToString()});
                    Check(bottom<=.9f,"unchanged ordinary camera leaves the entire front wall above the footer: "+size);
                    Check(min.X>=.02f&&max.X<=.98f&&min.Y>=.14f&&max.Y<=.9f,"core orchard landmarks fit the ordinary HUD-safe camera: "+size);
                }
                finally{view.Free();}
            }
            var columns=AuditDescendants(terrain).OfType<MeshInstance3D>().Where(n=>n.Name=="OrchardCarvedStone").ToArray();
            Check(columns.Length==5,"five original columns retain their carved-stone roles");
            foreach(var column in columns)
            {
                var local=column.Mesh.GetAabb();var parent=(Node3D)column.GetParent();
                int x=(int)parent.Position.X,z=(int)parent.Position.Z;
                Check(local.Position.X>=-.5f&&local.End.X<=.5f&&local.Position.Z>=-.5f&&local.End.Z<=.5f&&
                    Math.Abs(local.End.Y-(terrain.Layout.At(x,z).ObstacleHeight??.9f)*TempleStageView.HeightScale)<.0001f,
                    "carved stone keeps its exact authoritative footprint and crown");
            }
            foreach(var mesh in columns.Select(n=>n.Mesh).Concat(new[]{branch.GetNode<MeshInstance3D>("BowedOrchardWood").Mesh}).Distinct())
            for(int surface=0;surface<mesh.GetSurfaceCount();surface++)
            {
                var arrays=mesh.SurfaceGetArrays(surface);var v=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();bool valid=true;
                for(int i=0;i<ids.Length;i+=3){int a=ids[i],b=ids[i+1],c=ids[i+2];var cross=(v[b]-v[a]).Cross(v[c]-v[a]);valid&=cross.LengthSquared()>1e-10f&&cross.Dot(normals[a]+normals[b]+normals[c])<0;}
                Check(valid&&v.All(p=>p.IsFinite()),"owned carved-stone and branch triangles face outward with finite positions");
            }
            foreach(var quality in new[]{GraphicsPreset.High,GraphicsPreset.Medium,GraphicsPreset.Low})
            {
                await SelectGraphics(quality);Check(mill.IsVisibleInTree()&&branch.IsVisibleInTree(),"mill and orchard branch survive "+quality);
                await Capture("orchard-"+GraphicsQuality.ToWire(quality)+"-midday");
            }
            var sails=mill.GetNode<Node3D>("WindmillSails");var before=sails.Basis;await Frames(30);
            Check(!sails.Basis.IsEqualApprox(before),"Low retains real slow mill rotation");
            await KeyPress(Key.Escape);var paused=sails.Basis;var pausedBranch=branch.Basis;await Frames(12);
            Check(sails.Basis.IsEqualApprox(paused)&&branch.Basis.IsEqualApprox(pausedBranch),"one core breeze clock freezes mill and branch on local pause");await ActivateNamed("ResumeGame");
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Night);await Frames(90);await Capture("orchard-high-night-combat");
            await SelectLocalLighting(StageTime.Midday);
            var cameraActual=_root.GetViewport().GetCamera3D()!;var pose=cameraActual.Transform;
            try{cameraActual.Position=new(26,19,30);cameraActual.LookAt(new(9.5f,.5f,9.5f));await Capture("orchard-oblique-inspection");}
            finally{cameraActual.Transform=pose;}
        }
        finally{Input(0,default);_root.Navigate("Modes");await Frames(3);await SelectGraphics(preset);await SelectLocalLighting(lighting);}
    }

    private static IEnumerable<Vector3> OrchardAabbCorners(Aabb box)
    {
        foreach(int x in new[]{0,1})foreach(int y in new[]{0,1})foreach(int z in new[]{0,1})
            yield return box.Position+box.Size*new Vector3(x,y,z);
    }
}
