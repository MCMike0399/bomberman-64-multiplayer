using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private ShaderMaterial? _glacierPennantPaint;
    private OmniLight3D? _glacierWarmLight;

    private void BuildGlacierBowl()
    {
        var paint=new ShaderMaterial{Shader=new Shader{Code=GlacierBowlShader}};
        AddChild(new MeshInstance3D{Name="GlacierBowlBody",Mesh=GlacierBowlMesh(Width,Depth),MaterialOverride=paint});
        var blue=_art.Toy(new("6baccb"),.82f);
        // An attached shelf carries the arch; its inner edge remains outside
        // the original wall cells and never invents playable snow beyond them.
        RoundedBox(this,new(-2.9f,-.08f,4.4f),new(5.8f,.48f,7.3f),blue,.32f).Name="GlacierArchShelf";
        var arch=Prop(this,"GlacierArch",new(-4.1f,-.325f,4.4f),1.3f);
        foreach(int side in new[]{-1,1})
        {
            RoundedBox(arch,new(0,1.4f,side*1.65f),new(2.1f,2.8f,1.3f),blue,.32f);
            Sphere(arch,new(0,.15f,side*1.8f),new(2.8f,.7f,1.9f),_snowMaterial);
        }
        RoundedBox(arch,new(0,3.05f,0),new(2.3f,1.0f,4.7f),blue,.32f);
        Sphere(arch,new(0,3.57f,0),new(2.45f,.42f,4.9f),_snowMaterial);
        for(int i=0;i<5;i++)
        {
            float z=-1.65f+i*.82f,h=.55f+(i%3)*.23f;
            arch.AddChild(new MeshInstance3D{Name="GlacierHangingIce"+i,Mesh=IceConeMesh(i,h),
                Position=new(.9f,2.85f,z),Basis=PresentationMath.Rotation(new(Mathf.Pi,0,0),new(.55f,1,.55f)),MaterialOverride=_crystalMaterial});
        }
        for(int i=0;i<3;i++)
            arch.AddChild(new MeshInstance3D{Name="GlacierCrownIce"+i,Mesh=IceConeMesh(i,.75f+i*.22f),
                Position=new(0,3.63f,(i-1)*1.35f),Scale=new(1.35f,1,1.35f),MaterialOverride=blue});

        var mast=Prop(this,"GlacierWindLantern",new(Width+1.35f,.48f,Depth-3.4f));
        var wood=_art.Color(new("786478"),.87f);var brass=_art.Color(new("bc995c"),.58f);
        RoundedBox(mast,new(0,1.2f,0),new(.16f,2.4f,.16f),wood,.5f);
        Sphere(mast,new(0,2.48f,0),Vector3.One*.27f,brass);
        _glacierPennantPaint=new ShaderMaterial{Shader=new Shader{Code=GlacierPennantShader}};
        Plate(mast,new(0,2.15f,0),[new(0,0),new(1.25f,.04f),new(1.1f,-.49f),new(0,-.43f)],.035f,_glacierPennantPaint).Name="GlacierWindPennant";
        RoundedBox(mast,new(-.1f,.47f,.32f),new(.56f,.69f,.48f),wood,.19f);
        Box(mast,new(-.1f,.48f,.575f),new(.41f,.49f,.035f),Glow(new("ffc279"),1.5f),.045f).Name="GlacierWarmWindow";
        RoundedBox(mast,new(-.1f,.86f,.32f),new(.68f,.15f,.6f),brass,.5f);
        _glacierWarmLight=new OmniLight3D{Name="GlacierWarmLight",Position=new(-.1f,.6f,.78f),
            LightColor=new("ffcb8c"),LightEnergy=1.0f,OmniRange=6,ShadowEnabled=false,
            Visible=GraphicsQuality.Current!=GraphicsPreset.Low};mast.AddChild(_glacierWarmLight);
    }

    private void ProcessGlacierBowl()
    {
        if(_glacierPennantPaint!=null)
        {
            _glacierPennantPaint.SetShaderParameter("accepted_wind",_blizzardWind);
            _glacierPennantPaint.SetShaderParameter("weather_clock",_blizzardClock);
        }
        if(_glacierWarmLight!=null)_glacierWarmLight.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
    }

    private static ArrayMesh GlacierBowlMesh(int width,int depth)
    {
        const int cornerSteps=9,count=cornerSteps*4;
        float[] offset=[-.05f,2.7f,3.1f,2.8f,2.45f,1.65f,.7f,-.05f];
        float[] height=[-.32f,-.32f,-.08f,.14f,.47f,.68f,.31f,-.26f];
        var center=new Vector3(width/2f,0,depth/2f);
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int layer,int i)
        {
            i=(i+count)%count;int quadrant=i/cornerSteps,step=i%cornerSteps;
            float angle=quadrant*Mathf.Pi/2+step/(float)(cornerSteps-1)*Mathf.Pi/2;
            var(sin,cos)=PresentationMath.SinCos(angle);
            float radius=.6f+Math.Max(0,offset[layer])*.66f;
            float hx=width/2f+offset[layer]-radius,hz=depth/2f+offset[layer]-radius;
            float sx=quadrant is 0 or 3?1:-1,sz=quadrant<2?1:-1;
            float sculpt=layer is 4 or 5?.07f*PresentationMath.Sin(angle*3+.3f):0;
            return center+new Vector3(sx*hx+cos*radius,height[layer]+sculpt,sz*hz+sin*radius);
        }
        Vector3 Normal(int layer,int i)
        {
            int previous=(layer+offset.Length-1)%offset.Length,next=(layer+1)%offset.Length;
            return (Point(next,i)-Point(previous,i)).Cross(Point(layer,i+1)-Point(layer,i-1)).Normalized();
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc)
        {
            if((b-a).Cross(c-a).Dot(na+nb+nc)>=0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
            foreach(var(p,n) in new[]{(a,na),(b,nb),(c,nc)})
            {surface.SetNormal(n);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
        }
        for(int layer=0;layer<offset.Length;layer++)for(int i=0;i<count;i++)
        {
            int next=(layer+1)%offset.Length;
            var a=Point(layer,i);var b=Point(layer,i+1);var c=Point(next,i+1);var d=Point(next,i);
            Triangle(a,b,c,Normal(layer,i),Normal(layer,i+1),Normal(next,i+1));
            Triangle(a,c,d,Normal(layer,i),Normal(next,i+1),Normal(next,i));
        }
        surface.Index();return surface.Commit();
    }

    private const string GlacierBowlShader="""
shader_type spatial;
render_mode diffuse_burley;
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    float powder=smoothstep(.06,.29,world.y);
    float broad=.5+.5*sin(world.x*.24+world.z*.18);
    vec3 blue=mix(vec3(.12,.31,.43),vec3(.26,.53,.65),smoothstep(-.3,.15,world.y));
    ALBEDO=mix(blue,mix(vec3(.70,.82,.86),vec3(.88,.93,.94),broad),powder);
    ROUGHNESS=.88;SPECULAR=.19;
}
""";
    private const string GlacierPennantShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec2 accepted_wind=vec2(0.0);
uniform float weather_clock=0.0;
void vertex(){
    float along=VERTEX.x;
    vec2 direction=length(accepted_wind)>.04?accepted_wind:vec2(.3,.04);
    VERTEX.x=direction.x*along*.78;
    VERTEX.z+=direction.y*along*.78;
    VERTEX.y-=along*.15/(1.0+length(accepted_wind));
    VERTEX.y+=sin(weather_clock*3.0-along*3.0)*.045*along*min(1.0,length(accepted_wind));
    NORMAL=normalize(vec3(-direction.y,0.0,direction.x));
}
void fragment(){ALBEDO=mix(vec3(.54,.08,.16),vec3(.90,.38,.30),UV.y);ROUGHNESS=.85;}
""";
}
