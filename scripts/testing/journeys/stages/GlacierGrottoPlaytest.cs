using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task GlacierGrotto()
    {
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:7,SuddenDeath:false,RoundSeconds:300),1,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"grotto witness enters a real eight-player battle",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var body=terrain.GetNode<MeshInstance3D>("GlacierBowlBody");var crown=terrain.GetNode<MeshInstance3D>("GlacierGrottoCrown");
            Check(body.Mesh.GetSurfaceCount()==3&&terrain.GetNodeOrNull<Node3D>("GlacierArchShelf")==null&&terrain.GetNodeOrNull<Node3D>("GlacierArch")==null,
                "one carved body owns snow, vault ice and seated icicles without the old slab or posts");
            var hero=new List<Vector3>();int triangles=0;
            for(int surface=0;surface<body.Mesh.GetSurfaceCount();surface++)
            {
                var a=body.Mesh.SurfaceGetArrays(surface);var v=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                var role=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();bool front=true;
                for(int i=0;i<ids.Length;i+=3){int x=ids[i],y=ids[i+1],z=ids[i+2];var cross=(v[y]-v[x]).Cross(v[z]-v[x]);front&=cross.LengthSquared()>1e-10f&&cross.Dot(n[x]+n[y]+n[z])<0;triangles++;}
                Check(front&&v.All(p=>p.IsFinite())&&n.All(p=>p.IsFinite()),"captured grotto surface has finite clockwise outward triangles: "+surface);
                hero.AddRange(v.Where((p,i)=>role[i].X>.5f).Select(p=>body.ToGlobal(p)));
            }
            var crownArrays=crown.Mesh.SurfaceGetArrays(0);var crownVertices=crownArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            hero.AddRange(crownVertices.Select(p=>crown.ToGlobal(p)));
            var worldBounds=new Aabb(hero[0],Vector3.Zero);foreach(var point in hero)worldBounds=worldBounds.Expand(point);
            Event("grotto-captured-world-bounds",new{triangles,vertices=hero.Count,minimum=worldBounds.Position.ToString(),maximum=worldBounds.End.ToString()});
            Check(hero.All(p=>p.X<-.35f),"all principal grotto and crown vertices stay outside the playable standing margin");
            var opening=GrottoMeshHits(body,new(-3.75f,.65f,.5f),new(-3.75f,.65f,9.5f));
            Check(opening.Count==0,"actual mouth ray passes through both ends of the carved vault");
            var roof=GrottoMeshHits(body,new(-3.75f,.65f,5),new(-3.75f,7,5));
            var levels=roof.Select(p=>p.Y).DistinctBy(y=>MathF.Round(y*10000)).Order().ToArray();
            Event("grotto-vault-roof",new{levels});
            Check(levels.Length>=2&&levels[^1]-levels[0]>.5f,"actual vault ceiling and snow shoulder have real thickness");
            var ice=body.Mesh.SurfaceGetArrays(2);var iv=ice[(int)Mesh.ArrayType.Vertex].AsVector3Array();var tags=ice[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            var collars=iv.Where((p,i)=>tags[i].Y>.5f).Distinct().Select(p=>body.ToGlobal(p)).ToArray();float iceGap=0;
            foreach(var point in collars)
            {
                var hits=GrottoMeshHits(body,point+Vector3.Up*.08f,point-Vector3.Up*.08f,1);
                iceGap=Math.Max(iceGap,hits.Count==0?1:hits.Min(h=>h.DistanceTo(point)));
            }
            Check(collars.Length>=40&&iceGap<.035f,"all four rounded icicle collars are seated on the captured inner roof");
            var bases=crownVertices.Where(p=>Math.Abs(new Vector2(p.X,p.Z).Length()-.4f)<.00001f&&p.Y<.05f).Distinct().Select(p=>crown.ToGlobal(p)).ToArray();float crownGap=0;
            foreach(var point in bases)
            {
                var hits=GrottoMeshHits(body,point+Vector3.Up*.1f,point-Vector3.Up*.1f,0);
                crownGap=Math.Max(crownGap,hits.Count==0?1:hits.Min(h=>h.DistanceTo(point)));
            }
            Event("grotto-captured-contact",new{icicleCollars=collars.Length,iceGap,crownBaseVertices=bases.Length,crownGap});
            Check(bases.Length==7&&crownGap<.001f,"tilted crown crystal is supported by the actual captured snow surface");
            var lantern=terrain.GetNode<Node3D>("GlacierVaultLantern");
            var lampMeshes=AuditDescendants(lantern).OfType<MeshInstance3D>().ToArray();
            var lampPoints=lampMeshes.SelectMany(n=>Enumerable.Range(0,n.Mesh.GetSurfaceCount()).SelectMany(surface=>
                n.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(p=>n.ToGlobal(p)))).Distinct().ToArray();
            var lampBounds=new Aabb(lampPoints[0],Vector3.Zero);foreach(var point in lampPoints)lampBounds=lampBounds.Expand(point);
            var vaultTriangles=GrottoTriangles(body,0).Concat(GrottoTriangles(body,1)).Where(t=>
                Math.Min(t.A.X,Math.Min(t.B.X,t.C.X))<=lampBounds.End.X&&Math.Max(t.A.X,Math.Max(t.B.X,t.C.X))>=lampBounds.Position.X&&
                Math.Min(t.A.Z,Math.Min(t.B.Z,t.C.Z))<=lampBounds.End.Z&&Math.Max(t.A.Z,Math.Max(t.B.Z,t.C.Z))>=lampBounds.Position.Z).ToArray();
            float nearestIce=iv.Min(p=>body.ToGlobal(p).Z),lastLamp=lampPoints.Max(p=>p.Z);int embedded=0;var embeddedDetails=new List<object>();
            foreach(var point in lampPoints)
            {
                var heights=new List<float>();
                foreach(float y in GrottoTriangleHits(vaultTriangles,point+Vector3.Up*.0002f,point+Vector3.Up*10).Select(p=>p.Y).Order())
                    if(heights.Count==0||y-heights[^1]>.00001f)heights.Add(y);
                // Adjacent triangles share an exit. Quantized rounding can split
                // 1.0658500/1.0658501 into different bins at the rounding midpoint.
                if(heights.Count%2!=0){embedded++;embeddedDetails.Add(new{point=point.ToString(),heights});}
            }
            var window=lantern.GetNode<MeshInstance3D>("GlacierWarmWindow");var windowPoints=window.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()
                .Where(p=>p.Z>window.Mesh.GetAabb().End.Z-.0001f).Distinct().Select(p=>window.ToGlobal(p)).ToArray();
            var ordinary=_root.GetViewport().GetCamera3D()!;var allTriangles=GrottoTriangles(body);int visibleWindow=windowPoints.Count(p=>GrottoTriangleHits(allTriangles,ordinary.GlobalPosition,p).Count==0);
            Event("grotto-lantern-captured-clearance",new{lampVertices=lampPoints.Length,embeddedInVault=embedded,
                icicleSeparation=nearestIce-lastLamp,visibleWindow,windowSamples=windowPoints.Length,embeddedDetails=embeddedDetails.Take(3)});
            Check(nearestIce-lastLamp>.1f,"full lantern body and caps are separated from all actual icicle vertices");
            Check(embedded==0,"full captured lantern body and caps stay outside the solid vault shell");
            Check(visibleWindow>0,"ordinary battle camera retains an unobstructed actual-mesh warm-window sample");
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
            {
                var viewport=new SubViewport{Size=size,Disable3D=true};AddChild(viewport);
                try
                {
                    var camera=new Camera3D{Fov=36,Near=.1f,Far=180};viewport.AddChild(camera);
                    var rig=new BattleCameraRig(camera);rig.SetStage(_root.LatestStage!.Value);
                    Vector2 min=Vector2.One,max=Vector2.Zero;bool behind=false;
                    foreach(var point in hero){var screen=camera.UnprojectPosition(point)/new Vector2(size.X,size.Y);min=min.Min(screen);max=max.Max(screen);behind|=camera.IsPositionBehind(point);}
                    Event("grotto-ordinary-camera-bounds",new{size=size.ToString(),minimum=min.ToString(),maximum=max.ToString(),camera=camera.Position.ToString()});
                    Check(!behind&&min.X>=.025f&&max.X<=.975f&&min.Y>=.14f&&max.Y<=.9f,"principal grotto stays framed below the HUD in ordinary "+size+" battle camera");
                }
                finally{viewport.Free();}
            }
            foreach(var preset in new[]{GraphicsPreset.High,GraphicsPreset.Medium,GraphicsPreset.Low})
            {
                await SelectGraphics(preset);Check(body.IsVisibleInTree()&&crown.IsVisibleInTree(),"entire glacier body and crown survive "+preset);
                Check(terrain.GetNode<OmniLight3D>("GlacierVaultLantern/GlacierWarmLight").IsVisibleInTree()==(preset!=GraphicsPreset.Low),"grotto warm light remains emissive-only on Low");
                await Capture("grotto-"+GraphicsQuality.ToWire(preset)+"-midday");
            }
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Night);await Frames(90);
            await Capture("grotto-high-night-combat");await SelectLocalLighting(StageTime.Midday);
            var actual=_root.GetViewport().GetCamera3D()!;var pose=actual.Transform;
            try
            {
                actual.Position=new(-11,7,14);actual.LookAt(new(-3.4f,1.1f,5.5f));await Capture("grotto-front-three-quarter");
                actual.Position=new(-11,5,-5);actual.LookAt(new(-3.6f,1.1f,4.5f));await Capture("grotto-rear-support-inspection");
                // The ordinary rig reapplies its projection offsets every frame.
                // A close contact view needs its own zero-offset camera, not just a new pose.
                var low=new Camera3D{Fov=36,Near=.1f,Far=180,Current=true};AddChild(low);
                try
                {
                    low.Position=new(-3.55f,.45f,14f);low.LookAt(new(-3.75f,.7f,5.2f));low.MakeCurrent();await Frames(3);
                    var cavity=body.Mesh.SurfaceGetArrays(1);var points=cavity[(int)Mesh.ArrayType.Vertex].AsVector3Array()
                        .Select(p=>body.ToGlobal(p)).Where(p=>Math.Abs(p.Z-8.5f)<.0002f&&Math.Abs(p.X+3.75f)<=1.23f&&p.Y<=1.71f).Distinct().ToArray();
                    var viewport=_root.GetViewport().GetVisibleRect().Size;Vector2 min=Vector2.One,max=Vector2.Zero;bool behind=false;
                    foreach(var point in points){var at=low.UnprojectPosition(point)/viewport;min=min.Min(at);max=max.Max(at);behind|=low.IsPositionBehind(point);}
                    Event("grotto-low-inspection-visibility",new{current=low.IsCurrent(),openingVertices=points.Length,
                        minimum=min.ToString(),maximum=max.ToString(),camera=low.Position.ToString(),look=new Vector3(-3.75f,.7f,5.2f).ToString()});
                    Check(low.IsCurrent()&&points.Length>=24&&!behind&&min.X>=.04f&&max.X<=.96f&&min.Y>=.14f&&max.Y<=.9f&&
                        max.X-min.X>.3f&&max.Y-min.Y>.25f,"actual vault opening is prominently framed below the HUD by the independent low inspection camera");
                    Check(GrottoMeshHits(body,low.GlobalPosition,new(-3.75f,.7f,8.2f)).Count==0,
                        "low inspection camera has a clear actual-mesh sightline through the mouth");
                    await Capture("grotto-low-vault-inspection");
                }
                finally{low.Free();actual.MakeCurrent();}
            }
            finally{actual.Transform=pose;}
        }
        finally
        {Input(0,default);_root.Navigate("Modes");await Frames(3);await SelectGraphics(quality);await SelectLocalLighting(lighting);}
    }

    private static List<Vector3> GrottoMeshHits(MeshInstance3D node,Vector3 from,Vector3 to,int onlySurface=-1)=>
        GrottoTriangleHits(GrottoTriangles(node,onlySurface),from,to);

    private static List<(Vector3 A,Vector3 B,Vector3 C)> GrottoTriangles(MeshInstance3D node,int onlySurface=-1)
    {
        var result=new List<(Vector3 A,Vector3 B,Vector3 C)>();var transform=node.GlobalTransform;
        for(int surface=0;surface<node.Mesh.GetSurfaceCount();surface++)
        {
            if(onlySurface>=0&&surface!=onlySurface)continue;
            var arrays=node.Mesh.SurfaceGetArrays(surface);var v=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int i=0;i<ids.Length;i+=3)result.Add((transform*v[ids[i]],transform*v[ids[i+1]],transform*v[ids[i+2]]));
        }
        return result;
    }

    private static List<Vector3> GrottoTriangleHits(IEnumerable<(Vector3 A,Vector3 B,Vector3 C)> triangles,Vector3 from,Vector3 to)
    {
        var hits=new List<Vector3>();var direction=to-from;
        foreach(var (a,b,c) in triangles)
        {
            var e1=b-a;var e2=c-a;var h=direction.Cross(e2);float determinant=e1.Dot(h);if(Math.Abs(determinant)<.0000001f)continue;
            var offset=from-a;float u=offset.Dot(h)/determinant;if(u<-.00001f||u>1.00001f)continue;
            var q=offset.Cross(e1);float w=direction.Dot(q)/determinant;if(w<-.00001f||u+w>1.00001f)continue;
            float t=e2.Dot(q)/determinant;if(t>=0&&t<=1)hits.Add(from+direction*t);
        }
        return hits;
    }
}
