using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildCutawayHollow()
    {
        Name="FieldOfGrassTerrain";
        var soil=_art.HollowSurface(0);var turf=_art.HollowSurface(1);
        _hollowStoneMaterial=_art.HollowSurface(2);var outside=_art.HollowSurface(3);
        _fieldMaterials.AddRange([soil,turf,_hollowStoneMaterial,outside]);
        Box(this,new(Width/2f,-1.4f,Depth/2f),new(160,.2f,160),outside).Name="FieldExterior";

        // Only these sheets support gameplay. Their vertices are the exact
        // authority plane/ramp endpoints; sculpted loam stays below or outside.
        for(int z=1;z<Depth-1;z++)for(int x=1;x<Width-1;x++)
        {
            var cell=Layout.At(x,z);if(!cell.IsPassable)continue;
            float left=cell.Kind==CellKind.Ramp?(cell.RampDX<0?cell.RampToTier:cell.RampFromTier):cell.Tier;
            float right=cell.Kind==CellKind.Ramp?(cell.RampDX<0?cell.RampFromTier:cell.RampToTier):cell.Tier;
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            HollowQuad(surface,new(x,left*HeightScale,z),new(x+1,right*HeightScale,z),new(x+1,right*HeightScale,z+1),new(x,left*HeightScale,z+1),Vector3.Up);
            surface.Index();AddChild(new MeshInstance3D{Name=cell.Kind==CellKind.Ramp?$"MeadowBankSlope{x}_{z}":$"HollowGround{x}_{z}",
                Mesh=surface.Commit(),MaterialOverride=cell.Kind==CellKind.Ramp?soil:turf});
        }
        BuildHollowCutFace(soil);
        BuildHollowRidge(soil,left:true);BuildHollowRidge(soil,left:false);
        BuildHollowCoping(soil,front:true);
        // The far end closes with chunky dry stone rather than another fence.
        for(int x=0;x<Width;x++)
        {
            float support=HollowSupport(x+.5f);
            var root=new Node3D{Name=$"HollowDryStone{x}",Position=new(x+.5f,support,.5f)};AddChild(root);
            RoundedBox(root,new(0,.33f,0),new(.99f,.66f,.72f),_hollowStoneMaterial,.65f);
            RoundedBox(root,new(0,.73f,0),new(.92f,.25f,.65f),_hollowStoneMaterial,.75f);
        }
        BuildHollowRootsAndBurrow();
        BuildFieldGrass();
    }

    private float HollowSupport(float x)=>Layout.SurfaceAt(Math.Clamp(x,1.001f,Width-1.001f),Depth-1.5f)*HeightScale;
    private float HollowFront(float x)=>Depth-.05f+.07f*PresentationMath.Sin(x*.51f)+.035f*PresentationMath.Sin(x*1.3f);

    private void BuildHollowCutFace(Material soil)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const float step=.25f,bottom=-1.15f;
        for(float x=0;x<Width;x+=step)
        {
            float end=Math.Min(Width,x+step),hl=HollowSupport(x),hr=HollowSupport(end);
            for(float y=bottom;y<Math.Max(hl,hr);y+=step)
            {
                float yl=Math.Min(y,hl),yr=Math.Min(y,hr),tl=Math.Min(y+step,hl),tr=Math.Min(y+step,hr);
                float mx=(x+end)/2,my=(yl+yr+tl+tr)/4;
                // An actual opening below the playable floor, not a painted dot.
                if(MathF.Pow((mx-10.8f)/.43f,2)+MathF.Pow((my+.6f)/.29f,2)<1)continue;
                HollowQuad(surface,new(x,yl,HollowFront(x)),new(x,tl,HollowFront(x)),new(end,tr,HollowFront(end)),new(end,yr,HollowFront(end)),Vector3.Back);
            }
        }
        // The underside and side cuts make this a lifted slice, not a thin tray.
        HollowQuad(surface,new(0,bottom,0),new(Width,bottom,0),new(Width,bottom,Depth),new(0,bottom,Depth),Vector3.Down);
        foreach(bool left in new[]{true,false})
        {
            float x=left?0:Width;
            HollowQuad(surface,new(x,bottom,0),new(x,HollowSupport(x),0),new(x,HollowSupport(x),HollowFront(x)),new(x,bottom,HollowFront(x)),left?Vector3.Left:Vector3.Right);
        }
        surface.Index();AddChild(new MeshInstance3D{Name="HollowStrataCutFace",Mesh=surface.Commit(),MaterialOverride=soil});
    }

    private void BuildHollowRidge(Material soil,bool left)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int sections=44,arc=10;
        Vector3 Point(int section,int ring)
        {
            float z=section*(Depth/(float)sections),angle=ring*Mathf.Pi/arc;
            var (s,c)=PresentationMath.SinCos(angle);
            float swell=.91f+.09f*PresentationMath.Sin(z*.71f)+(left?.035f:-.035f)*PresentationMath.Sin(z*1.6f);
            float crown=2*HeightScale+.8775f;
            float y=2*HeightScale+.8775f*s;
            // The inside edge ends at x=1, outside the passable ledge. All
            // displacement faces away from the gameplay support sheet.
            float xx=left?Math.Min(.98f,.17f+c*.82f*swell):Math.Max(Width-.98f,Width-.17f-c*.82f*swell);
            return new(xx,Math.Min(crown,y),z);
        }
        for(int z=0;z<sections;z++)for(int r=0;r<arc;r++)
            HollowQuad(surface,Point(z,r),Point(z+1,r),Point(z+1,r+1),Point(z,r+1),Vector3.Up);
        foreach(int end in new[]{0,sections})for(int r=0;r<arc;r++)
            HollowTriangle(surface,new(left?.17f:Width-.17f,2*HeightScale,end==0?0:Depth),Point(end,r),Point(end,r+1),end==0?Vector3.Forward:Vector3.Back);
        // Rounded outer bellies continue down into the exposed cut section.
        for(int z=0;z<sections;z++)
        {
            float a=z*(Depth/(float)sections),b=(z+1)*(Depth/(float)sections);
            float xa=Point(z,arc).X,xb=Point(z+1,arc).X;
            HollowQuad(surface,new(xa,-1.15f,a),new(xa,2*HeightScale,a),new(xb,2*HeightScale,b),new(xb,-1.15f,b),left?Vector3.Left:Vector3.Right);
        }
        surface.Index();AddChild(new MeshInstance3D{Name=left?"HollowRoundedWestBank":"HollowRoundedEastBank",Mesh=surface.Commit(),MaterialOverride=soil});

    }

    private void BuildHollowCoping(Material soil,bool front)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int sections=84,arc=10;
        Vector3 Point(int section,int ring)
        {
            float x=section*Width/(float)sections,angle=ring*Mathf.Pi/arc;
            var(s,c)=PresentationMath.SinCos(angle);
            float slump=.83f+.03f*PresentationMath.Sin(x*.73f);
            return new(x,HollowSupport(x)+s*slump,(front?Depth-.5f:.5f)+c*.46f+PresentationMath.Sin(x*.61f)*.055f);
        }
        for(int x=0;x<sections;x++)for(int r=0;r<arc;r++)
            HollowQuad(surface,Point(x,r),Point(x+1,r),Point(x+1,r+1),Point(x,r+1),Vector3.Up);
        surface.Index();AddChild(new MeshInstance3D{Name="HollowOpenCutRim",Mesh=surface.Commit(),MaterialOverride=soil});
    }

    private void BuildHollowRootsAndBurrow()
    {
        var rootPaint=_art.Toy(new("603e30"),.94f);
        foreach(float side in new[]{2.1f,Width-2.3f})
        {
            float z=HollowFront(side)+.035f;
            HollowRoot(this,new(side,2.64f,z),new(side-.16f,1.75f,z+.025f),.17f,rootPaint);
            HollowRoot(this,new(side-.16f,1.75f,z+.025f),new(side+.23f,.78f,z+.035f),.15f,rootPaint);
            HollowRoot(this,new(side-.16f,1.75f,z+.025f),new(side-.7f,1.3f,z+.035f),.13f,rootPaint);
        }
        var tunnel=new SurfaceTool();tunnel.Begin(Mesh.PrimitiveType.Triangles);
        const int sides=20;float front=HollowFront(10.8f)+.02f;
        for(int i=0;i<sides;i++)
        {
            var(sa,ca)=PresentationMath.SinCos(i*Mathf.Tau/sides);var(sb,cb)=PresentationMath.SinCos((i+1)*Mathf.Tau/sides);
            var a=new Vector3(10.8f+ca*.47f,-.6f+sa*.32f,front);var b=new Vector3(10.8f+cb*.47f,-.6f+sb*.32f,front);
            HollowQuad(tunnel,a,b,new(b.X,b.Y,front-.45f),new(a.X,a.Y,front-.45f),new(10.8f-a.X,-.6f-a.Y,0));
            HollowTriangle(tunnel,new(10.8f,-.6f,front-.46f),new(a.X,a.Y,front-.46f),new(b.X,b.Y,front-.46f),Vector3.Back);
        }
        tunnel.Index();AddChild(new MeshInstance3D{Name="HollowBurrow",Mesh=tunnel.Commit(),MaterialOverride=_art.Color(new("2f3229"),1)});
        foreach(var p in new[]{new Vector3(3.7f,.75f,HollowFront(3.7f)+.01f),new Vector3(7.5f,-.55f,HollowFront(7.5f)+.01f),new Vector3(15.6f,-.45f,HollowFront(15.6f)+.01f)})
            Bead(this,p,new(.42f,.25f,.16f),_hollowStoneMaterial!).Name="HollowBuriedPebble";
    }

    private static void HollowRoot(Node parent,Vector3 a,Vector3 b,float width,Material material)
    {
        var up=(b-a).Normalized();var right=up.Cross(Vector3.Back).Normalized();var back=right.Cross(up);
        var root=RoundedBox(parent,(a+b)/2,new(width,a.DistanceTo(b),width),material,.8f);
        root.Basis=new(right,up,back);root.Name="HollowRoot";
    }

    private static void HollowQuad(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
    {HollowTriangle(surface,a,b,c,outward);HollowTriangle(surface,a,c,d,outward);}
    private static void HollowTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
    {
        var n=(c-a).Cross(b-a);if(n.LengthSquared()<1e-12f)return;n=n.Normalized();
        if(n.Dot(outward)<0){(b,c)=(c,b);n=-n;}
        foreach(var p in new[]{a,b,c}){surface.SetNormal(n);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
    }
}
