using Godot;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task OrreryKeepTour()
    {
        Event("orrery-keep-policy",new{authorityMutation=false,castleAndCourt="unchanged",composition="painted cosmic tabletop miniature"});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        if(!_headless)await ResizeWindow(new(1280,800));
        _root.StartLocal(new SessionSettings(Stage:BlastStages.TopRules,Bots:0,SuddenDeath:false),4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary four-player Orrery Keep battle starts",150);
        await SelectLocalLighting(StageTime.Midday);
        var arena=LightingArena;var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
        try
        {
            var problems=AuditModelProblems(terrain,out int meshes,out int vertices);
            Check(problems.Count==0,$"orrery and core plinth have finite nonempty geometry: {string.Join("; ",problems.Take(4))}");
            foreach(var meshNode in AuditDescendants(terrain).OfType<MeshInstance3D>().Where(n=>n.Name=="FortressCrag"||
                n.Name.ToString().Contains("OrreryArm",StringComparison.Ordinal)||n.Name.ToString().StartsWith("FourPointStar",StringComparison.Ordinal)||n.Name=="HeroPlanetRing"))
            {
                bool outward=true;int triangles=0;
                for(int surface=0;surface<meshNode.Mesh.GetSurfaceCount();surface++)
                {
                    var arrays=meshNode.Mesh.SurfaceGetArrays(surface);
                    var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var index=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Enumerable.Range(0,points.Length).ToArray():
                        arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    for(int i=0;i<index.Length;i+=3)
                    {
                        int a=index[i],b=index[i+1],c=index[i+2];
                        var cross=(points[b]-points[a]).Cross(points[c]-points[a]);
                        outward&=cross.LengthSquared()>1e-10f&&cross.Dot(normals[a]+normals[b]+normals[c])<0;triangles++;
                    }
                }
                Check(outward&&triangles>0,"actual owned orrery triangles are nondegenerate and clockwise: "+meshNode.Name);
            }
            Check(!AuditDescendants(terrain).Any(n=>n.Name.ToString().StartsWith("Cloud")),"Orrery Keep removes cumulus clouds from its cosmic setting");
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            {
                await SelectGraphics(preset);
                Check(terrain.GetNode<MeshInstance3D>("FortressCrag").IsVisibleInTree()&&terrain.GetNode<MeshInstance3D>("OrreryBackdrop").IsVisibleInTree(),"core plinth and painted background survive "+preset);
                Check(terrain.Scenery!.IsVisibleInTree()==(preset!=GraphicsPreset.Low),"orrery decoration follows scenery visibility at "+preset);
                if(!_headless)
                {
                    CheckFraming(BlastStages.TopRules,GetWindow().Size);
                    if(preset==GraphicsPreset.High)CheckOrreryPlanets(terrain);
                }
                await Capture("orrery-keep-"+GraphicsQuality.ToWire(preset)+"-battle");
            }
            await SelectLocalLighting(StageTime.Night);if(!_headless)CheckOrreryPlanets(terrain);
            else Event("orrery-headless-limit",new{projectionQualified=false,logicalCanvas=GetViewport().GetVisibleRect().Size.ToString()});
            await Capture("orrery-keep-high-night");
            if(!_headless)
            {
                var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;float h=camera.HOffset,v=camera.VOffset;
                camera.HOffset=0;camera.VOffset=0;camera.Position=new(31,21,34);camera.LookAt(new Vector3(9.5f,-1.8f,8.5f));
                await Capture("orrery-keep-establishing-inspection");camera.Transform=pose;camera.HOffset=h;camera.VOffset=v;
            }
            _root.Back();await Frames(3);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.TopRules,Bots:7,SuddenDeath:false,RoundSeconds:300),1,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player real Orrery Keep combat begins",180);
            bool bomb=false,blast=false;
            for(int i=0;i<330&&_root.ScreenName=="Match";i++)
            {
                bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;
                if(bomb&&blast)break;await Frames(1);
            }
            Check(bomb&&blast&&State.GetProperty("players").GetArrayLength()==8,"eight-player authority produces real bombs and explosions in the retained castle");
            await Capture("orrery-keep-eight-player-battle");
            if(!_headless){await SelectLocalLighting(StageTime.Night);await Capture("orrery-keep-eight-player-night");}
        }
        finally {_root.Back();await Frames(3);await SelectGraphics(quality);await SelectLocalLighting(lighting);}
    }

    private void CheckOrreryPlanets(TempleStageView terrain)
    {
        var camera=GetViewport().GetCamera3D()!;var viewport=GetViewport().GetVisibleRect().Size;
        var castle=AuditDescendants(terrain).OfType<MeshInstance3D>().Where(n=>n.Mesh!=null&&
            n.Name!="OrreryBackdrop"&&terrain.Scenery?.IsAncestorOf(n)!=true).ToArray();
        foreach(string name in new[]{"HeroPlanet","CompanionPlanet","ToyMoon"})
        {
            var globe=terrain.Scenery!.GetNode<MeshInstance3D>(name+"/Globe");var box=globe.Mesh.GetAabb();
            var points=Enumerable.Range(0,8).Select(i=>camera.UnprojectPosition(globe.ToGlobal(box.GetEndpoint(i)))/viewport).ToArray();
            var min=points.Aggregate(new Vector2(float.MaxValue,float.MaxValue),(a,b)=>a.Min(b));
            var max=points.Aggregate(new Vector2(float.MinValue,float.MinValue),(a,b)=>a.Max(b));
            Event("orrery-planet-framing",new{name,min=min.ToString(),max=max.ToString(),width=max.X-min.X});
            Check(min.X>=.025f&&max.X<=.975f&&min.Y>=.145f&&max.Y<=.90f,name+" is fully framed outside the HUD and footer");
            Check(max.X-min.X<=.12f,name+" stays at or below twelve percent of frame width");
            float radius=box.Size.X*.5f*globe.GlobalBasis.X.Length();
            var hits=new List<string>();
            foreach(var offset in new[]{Vector2.Zero,new Vector2(.7f,0),new Vector2(-.7f,0),
                new Vector2(0,.7f),new Vector2(0,-.7f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f),
                new Vector2(.5f,-.5f),new Vector2(-.5f,-.5f)})
            {
                float facing=Mathf.Sqrt(1-offset.LengthSquared());
                var target=globe.GlobalPosition+(camera.GlobalBasis.X*offset.X+camera.GlobalBasis.Y*offset.Y+
                    camera.GlobalBasis.Z*facing)*radius;
                foreach(var node in castle)
                    if(OrreryCastleOccludes(node,camera.GlobalPosition,target)){hits.Add(node.Name);break;}
            }
            Event("orrery-planet-occlusion",new{name,samples=9,blocked=hits.Count,occluders=hits.Take(4).ToArray()});
            Check(hits.Count==0,name+" visible-hemisphere samples are not hidden by the actual castle/crag mesh");
        }
        var ring=terrain.Scenery!.GetNode<MeshInstance3D>("HeroPlanet/HeroPlanetRing");
        var ringPoints=Enumerable.Range(0,8).Select(i=>camera.UnprojectPosition(ring.ToGlobal(ring.Mesh.GetAabb().GetEndpoint(i)))/viewport).ToArray();
        var ringMin=ringPoints.Aggregate(new Vector2(float.MaxValue,float.MaxValue),(a,b)=>a.Min(b));
        var ringMax=ringPoints.Aggregate(new Vector2(float.MinValue,float.MinValue),(a,b)=>a.Max(b));
        Check(ringMin.X>=.025f&&ringMax.X<=.975f&&ringMin.Y>=.145f&&ringMax.Y<=.90f,
            "the complete small hero ring stays framed outside HUD and footer");
    }

    // This is an occlusion check against the consumed rendered castle triangles,
    // not merely a screen AABB or the authority's collision cells.
    private static bool OrreryCastleOccludes(MeshInstance3D node,Vector3 eye,Vector3 target)
    {
        var inverse=node.GlobalTransform.AffineInverse();
        Vector3 from=inverse*eye,to=inverse*target,direction=to-from;
        var bounds=node.Mesh.GetAabb();float enter=0,leave=1;
        for(int axis=0;axis<3;axis++)
        {
            float d=direction[axis],origin=from[axis];
            if(Math.Abs(d)<1e-8f){if(origin<bounds.Position[axis]||origin>bounds.End[axis])return false;continue;}
            float a=(bounds.Position[axis]-origin)/d,b=(bounds.End[axis]-origin)/d;
            if(a>b)(a,b)=(b,a);enter=Math.Max(enter,a);leave=Math.Min(leave,b);
            if(enter>leave)return false;
        }
        for(int surface=0;surface<node.Mesh.GetSurfaceCount();surface++)
        {
            var arrays=node.Mesh.SurfaceGetArrays(surface);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var index=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Enumerable.Range(0,vertices.Length).ToArray():
                arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for(int i=0;i<index.Length;i+=3)
            {
                var a=vertices[index[i]];var edge1=vertices[index[i+1]]-a;var edge2=vertices[index[i+2]]-a;
                var p=direction.Cross(edge2);float determinant=edge1.Dot(p);if(Math.Abs(determinant)<1e-8f)continue;
                float reciprocal=1/determinant;var delta=from-a;float u=delta.Dot(p)*reciprocal;if(u<0||u>1)continue;
                var q=delta.Cross(edge1);float v=direction.Dot(q)*reciprocal;if(v<0||u+v>1)continue;
                float t=edge2.Dot(q)*reciprocal;if(t>.00001f&&t<.9999f)return true;
            }
        }
        return false;
    }
}
