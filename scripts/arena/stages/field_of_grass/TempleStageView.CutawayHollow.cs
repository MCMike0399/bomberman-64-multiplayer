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
                Mesh=surface.Commit(),MaterialOverride=turf,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        BuildHollowBankBodies(soil);
        BuildHollowLandscape(_art.HollowSurface(4),outside);
        BuildHollowCutFace(soil);
        // The far end closes with chunky dry stone rather than another fence.
        for(int x=0;x<Width;x++)
        {
            float support=HollowSupport(x+.5f);
            var root=new Node3D{Name=$"HollowDryStone{x}",Position=new(x+.5f,support,.5f)};AddChild(root);
            RoundedBox(root,new(0,.33f,0),new(.99f,.66f,.72f),_hollowStoneMaterial,.65f);
            RoundedBox(root,new(0,.73f,0),new(.92f,.25f,.65f),_hollowStoneMaterial,.75f);
        }
        BuildHollowRootsAndBurrow();
        BuildHollowStile();
        BuildHollowBurrowPeek();
        BuildFieldGrass();
    }

    private float HollowSupport(float x)=>Layout.SurfaceAt(Math.Clamp(x,1.001f,Width-1.001f),Depth-1.5f)*HeightScale;
    private float HollowFront(float x)=>Depth;
    private static float HollowSmooth(float a,float b,float x){float t=Math.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);}
    private static float HollowHill(float x,float z,float cx,float cz,float rx,float rz,float height)
    {float r=MathF.Pow((x-cx)/rx,2)+MathF.Pow((z-cz)/rz,2);return r<1?height*(1-r)*(1-r):0;}
    private float HollowShoulder(float x,float z,bool left)
    {
        float u=left?x:Width-x;
        float edge=.9775f*HollowSmooth(-1.5f,1,u);
        float hill=left?Math.Max(HollowHill(u,z,-1.2f,3.2f,1.7f,3.1f,.55f),Math.Max(HollowHill(u,z,-2,9.8f,2.5f,4,.9f),HollowHill(u,z,-.8f,15.4f,1.6f,2.9f,.38f)))
            :Math.Max(HollowHill(u,z,-1.1f,4.8f,1.9f,3.6f,.42f),Math.Max(HollowHill(u,z,-1.9f,11.5f,2.4f,3.5f,.78f),HollowHill(u,z,-.8f,16.2f,1.7f,2.8f,.6f)));
        return 2.6f+Math.Max(edge,hill);
    }
    private float HollowLandscapeHeight(float x,float z)
    {
        if(x<=1)return HollowShoulder(x,z,true);
        if(x>=Width-1)return HollowShoulder(x,z,false);
        float support=HollowSupport(x);
        if(z<1)return Mathf.Lerp(2.6f,support+.855f,HollowSmooth(-3,.5f,z));
        // The actual boundary crown remains inside its nonpassable row. The
        // exposed front drops to a thin sod edge, rather than a coping roll.
        float h=support+.15f+.7275f*(1-HollowSmooth(Depth-.5f,Depth,z));
        if(x<1.5f)h=Mathf.Lerp(HollowShoulder(1,z,true),h,HollowSmooth(1,1.5f,x));
        if(x>Width-1.5f)h=Mathf.Lerp(h,HollowShoulder(Width-1,z,false),HollowSmooth(Width-1.5f,Width-1,x));
        return h;
    }
    private void BuildHollowLandscape(ShaderMaterial plateau,Material foreground)
    {
        _fieldMaterials.Add(plateau);
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        mesh.SetSmoothGroup(0);
        for(float z=IncludeScenery?-6:0;z<Depth;z+=.5f)for(float x=IncludeScenery?-6:0;x<(IncludeScenery?Width+6:Width);x+=.5f)
        {
            if(x>=1&&x<Width-1&&z>=1&&z<Depth-1)continue;
            // The rounded inner shoulder owns this half-cell strip once.
            // Overlaying a ragged lip on the plateau made their faces cross.
            if(z>=1&&z<Depth-1&&(x==.5f||x==Width-1))continue;
            Vector3 P(float xx,float zz)=>new(xx,HollowLandscapeHeight(xx,zz),zz);
            HollowQuad(mesh,P(x,z),P(x+.5f,z),P(x+.5f,z+.5f),P(x,z+.5f),Vector3.Up);
        }
        if(IncludeScenery)
        {
            HollowQuad(mesh,new(-80,2.6f,-80),new(-6,2.6f,-80),new(-6,2.6f,Depth),new(-80,2.6f,Depth),Vector3.Up);
            HollowQuad(mesh,new(Width+6,2.6f,-80),new(80,2.6f,-80),new(80,2.6f,Depth),new(Width+6,2.6f,Depth),Vector3.Up);
            HollowQuad(mesh,new(-6,2.6f,-80),new(Width+6,2.6f,-80),new(Width+6,2.6f,-6),new(-6,2.6f,-6),Vector3.Up);
            Box(this,new(Width/2f,-2.0f,Depth+35),new(160,.2f,70),foreground).Name="HollowForeground";
        }
        mesh.GenerateNormals();mesh.Index();AddChild(new MeshInstance3D{Name="HollowMeadowPlateau",Mesh=mesh.Commit(),MaterialOverride=plateau});
        BuildHollowSideCuts();
    }

    private void BuildHollowBankBodies(Material soil)
    {
        // Closed, slightly recessed earth volumes cast the bank silhouette.
        // The exact support sheets receive shadows but never self-shadow one
        // another at their per-cell seams in grazing Night illumination.
        foreach(bool left in new[]{true,false})
        {
            float outer=left?1:Width-1,inner=left?5:16;
            var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);
            Vector3 P(float x,float z)=>new(x,Surface(x,z)-.10f,z);
            for(float x=Math.Min(outer,inner);x<Math.Max(outer,inner);x++)
            {
                HollowQuad(body,P(x,1),P(x+1,1),P(x+1,Depth-1),P(x,Depth-1),Vector3.Up);
                foreach(float z in new[]{1f,Depth-1f})
                    HollowQuad(body,new(x,-1.8f,z),P(x,z),P(x+1,z),new(x+1,-1.8f,z),z<Depth/2f?Vector3.Forward:Vector3.Back);
            }
            HollowQuad(body,new(outer,-1.8f,1),new(outer,-1.8f,Depth-1),P(outer,Depth-1),P(outer,1),left?Vector3.Left:Vector3.Right);
            HollowQuad(body,new(inner,-1.8f,1),P(inner,1),P(inner,Depth-1),new(inner,-1.8f,Depth-1),left?Vector3.Right:Vector3.Left);
            HollowQuad(body,new(outer,-1.8f,1),new(inner,-1.8f,1),new(inner,-1.8f,Depth-1),new(outer,-1.8f,Depth-1),Vector3.Down);
            body.Index();AddChild(new MeshInstance3D{Name=left?"HollowLeftBankEarth":"HollowRightBankEarth",Mesh=body.Commit(),MaterialOverride=soil});
        }
    }

    private void BuildHollowSideCuts()
    {
        var paint=_art.HollowSurface(6);_fieldMaterials.Add(paint);
        var cap=new SurfaceTool();cap.Begin(Mesh.PrimitiveType.Triangles);cap.SetSmoothGroup(0);
        foreach(bool left in new[]{true,false})for(float z=1;z<Depth-1;z+=.5f)
        {
            Vector3 P(float u,float zz)
            {
                float x=left?u:Width-u,outer=left?.5f:Width-.5f,inner=left?1:Width-1;
                float start=HollowLandscapeHeight(outer,zz),crest=3.5775f;
                float height=u<=.7f?Mathf.Lerp(start,crest,HollowSmooth(.5f,.7f,u)):
                    2*HeightScale+.8775f*MathF.Sqrt(Math.Max(0,1-MathF.Pow((u-.7f)/.3f,2)));
                // At both ends this meets the exact adjacent plateau edge.
                float boundary=Math.Max(1-HollowSmooth(1,1.5f,zz),HollowSmooth(Depth-1.5f,Depth-1,zz));
                float end=Mathf.Lerp(start,HollowLandscapeHeight(inner,zz),(u-.5f)/.5f);
                return new(x,Mathf.Lerp(height,end,boundary),zz);
            }
            const int sections=12;
            for(int i=0;i<sections;i++)
            {
                float a=.5f+i*.5f/sections,b=.5f+(i+1)*.5f/sections;
                HollowQuad(cap,P(a,z),P(b,z),P(b,z+.5f),P(a,z+.5f),Vector3.Up);
            }
            float edge=left?1:Width-1;
            HollowQuad(cap,new(edge,2*HeightScale,z),P(1,z),P(1,z+.5f),new(edge,2*HeightScale,z+.5f),left?Vector3.Right:Vector3.Left);
        }
        cap.GenerateNormals();cap.Index();
        AddChild(new MeshInstance3D{Name="HollowSideEarthCuts",Mesh=cap.Commit(),MaterialOverride=paint});
    }
    private float HollowLip(float x)
    {
        float overhang=.32f;
        foreach(float notch in new[]{4.8f,8.4f,13.3f,17.1f})overhang-=.18f*Math.Max(0,1-Math.Abs(x-notch)/.45f);
        return Math.Clamp(overhang,.15f,.4f);
    }
    private void BuildHollowCutFace(Material soil)
    {
        float left=IncludeScenery?-80:0,right=IncludeScenery?80:Width,bottom=IncludeScenery?-1.8f:-.68f;
        var face=new SurfaceTool();face.Begin(Mesh.PrimitiveType.Triangles);
        var sod=new SurfaceTool();sod.Begin(Mesh.PrimitiveType.Triangles);sod.SetSmoothGroup(0);
        for(float x=left;x<right;x+=.5f)
        {
            float end=Math.Min(right,x+.5f),a=HollowLandscapeHeight(x,Depth),b=HollowLandscapeHeight(end,Depth);
            HollowQuad(face,new(x,bottom,Depth),new(x,a,Depth),new(end,b,Depth),new(end,bottom,Depth),Vector3.Back);
            // A thick rolled turf edge grows from the cut face's own boundary.
            // It has no overlapping top sheet and no hard two-plane crease.
            Vector3 Roll(float xx,float height,int ring)
            {
                var(s,c)=PresentationMath.SinCos(ring*Mathf.Pi/8);
                return new(xx,height-.18f+c*.18f,Depth+s*HollowLip(xx));
            }
            for(int ring=0;ring<8;ring++)
            {
                var(s,c)=PresentationMath.SinCos((ring+.5f)*Mathf.Pi/8);
                HollowQuad(sod,Roll(x,a,ring),Roll(end,b,ring),Roll(end,b,ring+1),Roll(x,a,ring+1),new(0,c,s));
            }
        }
        face.Index();sod.GenerateNormals();sod.Index();AddChild(new MeshInstance3D{Name="HollowStrataCutFace",Mesh=face.Commit(),MaterialOverride=soil});
        var sodPaint=_art.HollowSurface(5);_fieldMaterials.Add(sodPaint);
        AddChild(new MeshInstance3D{Name="HollowRaggedSodLip",Mesh=sod.Commit(),MaterialOverride=sodPaint});
        if(!IncludeScenery)return;
        foreach(float x in new[]{4.8f,8.4f,13.3f,17.1f})
            RoundedBox(this,new(x,-1.65f,Depth+.55f),new(.5f,.3f,.4f),soil,.9f).Name="HollowDroppedClod";
    }
    private void BuildHollowRootsAndBurrow()
    {
        if(!IncludeScenery)return;
        var paint=_art.Color(new("593b2c"),.96f);
        var curves=new[]{
            (new Vector3(1.4f,2.78f,Depth+.12f),new Vector3(.8f,2.1f,Depth+.18f),new Vector3(2.2f,1.1f,Depth+.21f),new Vector3(1.9f,.32f,Depth+.19f)),
            (new Vector3(2.65f,2.77f,Depth+.14f),new Vector3(3.1f,2.35f,Depth+.20f),new Vector3(2.2f,1.45f,Depth+.23f),new Vector3(2.8f,.78f,Depth+.22f)),
            (new Vector3(18.7f,2.77f,Depth+.11f),new Vector3(19.2f,2.2f,Depth+.19f),new Vector3(17.8f,1.2f,Depth+.23f),new Vector3(18.5f,.28f,Depth+.21f)),
            (new Vector3(19.55f,2.75f,Depth+.13f),new Vector3(19.2f,2.1f,Depth+.22f),new Vector3(20.1f,1.75f,Depth+.25f),new Vector3(19.75f,1.05f,Depth+.23f))};
        int root=0;foreach(var(a,b,c,d)in curves)BuildHollowRootCurve(a,b,c,d,paint,root++);
        // An inset ellipse and its matching raised lip cover an intact cut face.
        // There are no skipped quads or gaps to the background.
        var mouth=new SurfaceTool();mouth.Begin(Mesh.PrimitiveType.Triangles);
        var rim=new SurfaceTool();rim.Begin(Mesh.PrimitiveType.Triangles);
        const int sides=32;
        for(int i=0;i<sides;i++)
        {
            var(sa,ca)=PresentationMath.SinCos(i*Mathf.Tau/sides);var(sb,cb)=PresentationMath.SinCos((i+1)*Mathf.Tau/sides);
            Vector3 P(float co,float si,float rx,float ry,float z)=>new(10.8f+co*rx,-.88f+si*ry,z);
            var a=P(ca,sa,.6f,.4f,Depth+.025f);var b=P(cb,sb,.6f,.4f,Depth+.025f);
            HollowTriangle(mouth,new(10.8f,-.88f,Depth+.025f),a,b,Vector3.Back);
            HollowQuad(rim,P(ca,sa,.73f,.52f,Depth+.08f),P(cb,sb,.73f,.52f,Depth+.08f),P(cb,sb,.6f,.4f,Depth+.15f),P(ca,sa,.6f,.4f,Depth+.15f),Vector3.Back);
        }
        mouth.Index();rim.Index();AddChild(new MeshInstance3D{Name="HollowBurrowInset",Mesh=mouth.Commit(),MaterialOverride=_art.Color(new("27291e"),1)});
        AddChild(new MeshInstance3D{Name="HollowBurrowEarthLip",Mesh=rim.Commit(),MaterialOverride=_art.Color(new("9a623c"),.95f)});
        foreach(float cluster in new[]{2.7f,9.5f,17.4f})for(int n=0;n<3;n++)
            Bead(this,new(cluster+(n-1)*.72f,-1.04f+(n%2)*.10f,Depth+.045f),new(.85f,.42f,.18f),_hollowStoneMaterial!).Name="HollowClayPebble";
    }
    private void BuildHollowRootCurve(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Material paint,int id)
    {
        const int sections=16,sides=8;var rings=new Vector3[sections+1,sides];var centres=new Vector3[sections+1];
        for(int row=0;row<=sections;row++)
        {
            float t=row/(float)sections,u=1-t;var point=a*u*u*u+b*3*u*u*t+c*3*u*t*t+d*t*t*t;
            var tangent=((b-a)*3*u*u+(c-b)*6*u*t+(d-c)*3*t*t).Normalized();
            var right=tangent.Cross(Vector3.Back).Normalized();var back=right.Cross(tangent);centres[row]=point;
            float radius=Mathf.Lerp(.15f,.04f,t);
            for(int i=0;i<sides;i++){var(s,co)=PresentationMath.SinCos(i*Mathf.Tau/sides);rings[row,i]=point+(right*co+back*s)*radius;}
        }
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        for(int row=0;row<sections;row++)for(int i=0;i<sides;i++)
        {int j=(i+1)%sides;HollowQuad(mesh,rings[row,i],rings[row,j],rings[row+1,j],rings[row+1,i],(rings[row,i]+rings[row,j])/2-centres[row]);}
        mesh.Index();AddChild(new MeshInstance3D{Name="HollowCurvedRoot"+id,Mesh=mesh.Commit(),MaterialOverride=paint});
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
