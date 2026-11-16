using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private ShaderMaterial? _coastalBeam;
    private StandardMaterial3D? _pierSconceGlow;

    /// <summary>A Blue Resort-informed coast around the suspended piers: a lighthouse
    /// on a rocky islet with a jetty, grass-capped sea stacks, channel buoys, a
    /// sailboat and gulls. All of it stands in open sea outside the arena.</summary>
    private void BuildUpAndDownExterior()
    {
        var scenery=_scenery!;
        float sea=-.53f;
        var cliff=_art.Color(new("8c8f8a"),.95f);var cliffDark=_art.Color(new("6d726f"),.95f);
        var grass=_art.Color(new("74a65a"),.95f);var plank=_art.Color(new("9a7650"),.85f);

        // Lighthouse islet behind the west pier.
        var islet=new Vector3(-5,sea,-1.0f);
        var coreIslet=Prop(this,"LighthouseIsletCore",Vector3.Zero);
        Rock(coreIslet,islet+new Vector3(0,-.55f,0),new(4.6f,1.9f,4.2f),cliff,0,.3f);
        Rock(coreIslet,islet+new Vector3(1.7f,-.5f,1.3f),new(2.2f,1.2f,2),cliffDark,2,1.1f);
        Rock(coreIslet,islet+new Vector3(-1.6f,-.5f,1),new(1.8f,1,1.6f),cliffDark,3,2.3f);
        Sphere(coreIslet,islet+new Vector3(-.1f,.68f,-.1f),new(2.6f,.36f,2.4f),grass).Name="IsletTurf";
        var lighthouse=Lighthouse(coreIslet,islet+new Vector3(-.2f,.75f,-.3f),4.6f,0,coreClock:true);
        _coastalBeacon=lighthouse.GetNode<Node3D>("LighthouseBeam");
        _coastalLens=new StandardMaterial3D{AlbedoColor=new("fff1b5"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled=true,Emission=new("fff1b5"),EmissionEnergyMultiplier=1.2f,DisableFog=true};
        lighthouse.GetNode<MeshInstance3D>("LighthouseLens").MaterialOverride=_coastalLens;
        var jetty=Prop(scenery,"Jetty",islet+new Vector3(2.2f,0,2.4f),.55f);
        for(int i=0;i<6;i++)Box(jetty,new(0,.28f,i*.42f),new(.9f,.08f,.38f),plank,.015f);
        foreach(int side in new[]{-1,1})for(int post=0;post<3;post++)
            Cylinder(jetty,new(side*.42f,-.05f,post*1.05f),.07f,1.1f,_art.Color(new("6b4d33"),.85f));
        Rowboat(scenery,islet+new Vector3(3.6f,0,4.1f),1.2f,new("2f7fb8"),.6f);

        // Same four coastal landmarks, with weathered ledges and unequal outlines
        // instead of straight tubes and circular turf caps.
        var stackPaint=new StandardMaterial3D {AlbedoColor=Colors.White,VertexColorUseAsAlbedo=true,VertexColorIsSrgb=true,Roughness=.95f};
        void Stack(Vector3 at,float height,float width,int variant,bool tree)
        {
            var stack=Prop(scenery,"SeaStack",at);
            stack.Basis=PresentationMath.Rotation(new(0,variant*1.3f,0));
            string key=$"coastal-stack:{height}:{width}:{variant}";
            if(!SceneryMeshes.TryGetValue(key,out var mesh))
                SceneryMeshes[key]=mesh=CoastalSeaStackMesh(height,width,variant);
            stack.AddChild(new MeshInstance3D{Name="ErodedSeaStack",Mesh=mesh,MaterialOverride=stackPaint});
            if(tree)RoundTree(stack,new(.1f,height+.04f,0),.7f,new("5b9a4b"),variant);
        }
        var seaArch=Prop(this,"TideglassSeaArch",new(Width+3.6f,sea,-1.5f));
        seaArch.Basis=PresentationMath.Rotation(new(0,-.22f,0));
        seaArch.AddChild(new MeshInstance3D{Name="CarvedCoastalArch",Mesh=CoastalArchMesh(5.2f,2.4f),MaterialOverride=stackPaint});
        Stack(new(Width+6.2f,sea,1.4f),3.2f,1.8f,1,false);
        Stack(new(Width+3.3f,sea,12.5f),2.1f,1.6f,2,false);
        Stack(new(-3.4f,sea,9.5f),1.4f,1.4f,3,false);

        BuildCoastalBellBuoy(new(-1.8f,sea,5));
        Buoy(scenery,new(Width+1.8f,sea,7),new("3f9b4f"),.8f);
        Buoy(scenery,new(-2.2f,sea,Depth-.5f),new("d8433b"),1.6f);
        Buoy(scenery,new(Width+2.2f,sea,Depth+.2f),new("3f9b4f"),2.3f);
        Sailboat(scenery,new(-4.8f,sea,17),-.4f,new("f2f0e6"),new("f4b23e"),.9f);

        Gull(scenery,new(-5,7.8f,-1.6f),2.4f,.6f,0);
        Gull(scenery,new(-5,8.3f,-1.6f),3.1f,.5f,2.2f);
        Gull(scenery,new(Width+4.5f,8.6f,0),2.6f,-.55f,1);

        // Far coastline: a low headland with a scattering of resort rooftops.
        Hill(scenery,new(Width*.4f,-1.4f,-30),new(40,7,10),new("7c8b7d"));
        Hill(scenery,new(Width*.4f,-.4f,-29),new(30,6,8),new("6c9b5e"));
        for(int i=0;i<6;i++)
        {
            var house=Prop(scenery,"DistantHouse",new(Width*.4f-9+i*3.4f,1.6f+(i%3)*.4f,-26.8f+(i%2)*.8f),i*.4f);
            Box(house,Vector3.Zero,new(1.6f,1.2f,1.4f),_art.Color(new("ece3cf"),.9f));
            Cone(house,new(0,.95f,0),1.25f,.75f,_art.Color(new(i%2==0?"3e6f8e":"c2573f"),.7f),4).Rotation=new(0,Mathf.Pi/4,0);
        }
    }
    private void SetUpAndDownLighting(StageLighting lighting)
    {
        bool night=lighting.Label=="Night";
        if(_coastalBeam!=null)_coastalBeam.SetShaderParameter("night_strength",night?.12f:0f);
        if(_pierSconceGlow!=null)
        {
            _pierSconceGlow.AlbedoColor=new(night?"edbf7c":"9b7847");
            _pierSconceGlow.EmissionEnergyMultiplier=night?1.2f:0;
        }
    }

    private ShaderMaterial CoastalBeamPaint()
    {
        _coastalBeam=new ShaderMaterial {Shader=new Shader {Code="""
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never;
uniform float night_strength=0.0;
void fragment(){
    float along=smoothstep(.0,.08,UV.x)*(1.0-smoothstep(.12,1.0,UV.x));
    // View-facing density fades continuously at the silhouette of the cone.
    float soft_edge=smoothstep(.0,.55,abs(dot(normalize(NORMAL),normalize(VIEW))));
    ALBEDO=vec3(.96,.84,.58);
    ALPHA=along*soft_edge*night_strength;
}
"""}};
        return _coastalBeam;
    }

    private static ArrayMesh CoastalBeamMesh()
    {
        const int sides=20;const float length=6.2f,small=.08f,large=.72f;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N,Vector2 UV) At(int end,int side)
        {
            var (sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
            float radius=end==0?small:large;
            return (new(.2f+end*length,cos*radius,sin*radius),new Vector3(-(large-small)/length,cos,sin).Normalized(),new(end,side/(float)sides));
        }
        void Triangle((Vector3 P,Vector3 N,Vector2 UV) a,(Vector3 P,Vector3 N,Vector2 UV) b,(Vector3 P,Vector3 N,Vector2 UV) c)
        {
            if((b.P-a.P).Cross(c.P-a.P).Dot(a.N+b.N+c.N)>0)(b,c)=(c,b);
            foreach(var v in new[]{a,b,c}){surface.SetNormal(v.N);surface.SetUV(v.UV);surface.AddVertex(v.P);}
        }
        for(int side=0;side<sides;side++)
        {
            var a=At(0,side);var b=At(1,side);var c=At(1,side+1);var d=At(0,side+1);
            Triangle(a,b,c);Triangle(a,c,d);
        }
        surface.Index();return surface.Commit();
    }

    /// <summary>An original low-poly coastal stack: broad foot, broken ledges,
    /// asymmetrical crown and restrained mineral/algae tint, all owned packed geometry.</summary>
    private static ArrayMesh CoastalSeaStackMesh(float height,float width,int variant)
    {
        const int sides=9;
        float[] levels=[-1,-.06f,height*.17f,height*.21f,height*.47f,height*.51f,height*.73f,height*.77f,height];
        float[] radii=[1.15f,1.12f,1.03f,1.10f,.85f,.94f,.78f,.84f,.67f];
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int ring,int side)
        {
            float angle=side*Mathf.Tau/sides,progress=Math.Max(0,levels[ring]/height);
            var (sin,cos)=PresentationMath.SinCos(angle);
            float uneven=1+.12f*PresentationMath.Sin(angle*3+variant*1.7f)+
                .06f*PresentationMath.Sin(angle*5+ring*.6f+variant);
            float radius=width*.48f*radii[ring]*uneven;
            // Gradual lean and displaced strata remove the lathed silhouette.
            float dx=width*.09f*progress*PresentationMath.Sin(variant+ring*.23f);
            float dz=width*.07f*progress*PresentationMath.Sin(variant*1.4f+ring*.31f);
            return new(cos*radius+dx,levels[ring],sin*radius+dz);
        }
        Vector3 Normal(int ring,int side)
        {
            var vertical=Point(Math.Min(levels.Length-1,ring+1),side)-Point(Math.Max(0,ring-1),side);
            var tangent=Point(ring,side+1)-Point(ring,side-1);
            return vertical.Cross(tangent).Normalized();
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color paint,Vector3 na,Vector3 nb,Vector3 nc)
        {
            if((b-a).Cross(c-a).Dot(na+nb+nc)>0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
            foreach(var (point,normal) in new[]{(a,na),(b,nb),(c,nc)})
            {surface.SetNormal(normal);surface.SetColor(paint);surface.SetUV(new(point.X,point.Y));surface.AddVertex(point);}
        }
        for(int ring=0;ring<levels.Length-1;ring++)for(int side=0;side<sides;side++)
        {
            var a=Point(ring,side);var b=Point(ring,side+1);
            var c=Point(ring+1,side+1);var d=Point(ring+1,side);
            // A broad darker tide foot, pale sediment ledges and sparse sage
            // weathering keep the rock quiet at the ordinary battle distance.
            float grain=PresentationMath.Sin(side*2.1f+variant*1.3f)*.025f;
            var color=new Color("8d9489")*(1+grain+(ring%2==0?-.035f:.04f));
            if(ring==0)color=new Color("657d73");
            else if(ring is 2 or 4 or 6)color=color.Lightened(.075f);
            if(ring>=5&&side%4==variant%4)color=color.Lerp(new Color("85916d"),.24f);
            Triangle(a,b,c,color,Normal(ring,side),Normal(ring,side+1),Normal(ring+1,side+1));Triangle(a,c,d,color,Normal(ring,side),Normal(ring+1,side+1),Normal(ring+1,side));
        }
        var center=new Vector3(0,height+.018f,0);
        for(int side=0;side<sides;side++)
        {
            var a=Point(levels.Length-1,side)+new Vector3(0,.012f,0);
            var b=Point(levels.Length-1,side+1)+new Vector3(0,.012f,0);
            var turf=new Color("789b63")*(1+PresentationMath.Sin(side*1.7f+variant)*.035f);
            Triangle(center,a,b,turf,Vector3.Up,Vector3.Up,Vector3.Up);
        }
        surface.Index();return surface.Commit();
    }

}
