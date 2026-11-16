using BlastCircuit.Rendering;
using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D? _orreryLowMoon;
    private readonly List<(Node3D Node,Transform3D Anchor,Vector3 Pivot)> _orreryOrbitNodes=[];
    private float _orreryOrbitClock;

    private void TrackOrreryOrbit(Node3D node,Vector3 pivot)=>_orreryOrbitNodes.Add((node,node.Transform,pivot));
    private void ProcessOrrery(float delta)
    {
        if(!WeatherActive)return;
        _orreryOrbitClock+=delta;
        var basis=PresentationMath.Rotation(new(0,.025f*PresentationMath.Sin(_orreryOrbitClock*.18f),0));
        foreach(var(node,anchor,pivot) in _orreryOrbitNodes)
            node.Transform=new Transform3D(basis,pivot-basis*pivot)*anchor;
    }

    private Vector3 OrrerySeat(float angle)
    {
        var point=CragPoint(Width,Depth,0,angle);
        var outward=(point-new Vector3(Width/2f,point.Y,Depth/2f)).Normalized();
        return point+outward*.16f+Vector3.Up*.02f;
    }
    private readonly List<OmniLight3D> _orreryWarmLights=[];

    private void UpdateOrreryCoreQuality()
    {
        if(_orreryLowMoon!=null)_orreryLowMoon.Visible=GraphicsQuality.Current==GraphicsPreset.Low;
        foreach(var light in _orreryWarmLights)light.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
    }

    private void BuildOrreryWallLamps()
    {
        foreach(float x in new[]{5.5f,13.5f})
        {
            var lamp=Prop(this,"OrreryInnerWallLantern",new(x,3.35f,1.96f));
            Box(lamp,Vector3.Zero,new(.7f,.95f,.16f),_art.Color(new("b09255")),.08f);
            Box(lamp,new(0,0,.09f),new(.51f,.72f,.045f),Glow(new("ffc484"),1.35f),.045f);
            Box(lamp,new(0,0,.12f),new(.045f,.73f,.035f),_art.Color(new("796440")));
            var light=new OmniLight3D{Name="OrreryWarmWallLight",Position=new(0,0,.5f),
                LightColor=new("ffd0a0"),LightEnergy=.7f,OmniRange=7,ShadowEnabled=false,
                Visible=GraphicsQuality.Current!=GraphicsPreset.Low};lamp.AddChild(light);_orreryWarmLights.Add(light);
        }
    }

    private void BuildOrreryCore()
    {
        _orreryLowMoon=Prop(this,"OrreryLowMoon",Vector3.Zero);
        var moonAt=new Vector3(-6.6f,-2.6f,7.1f);
        var moonPaint=new ShaderMaterial{Shader=new Shader{Code=OrreryGlobeShader}};
        moonPaint.SetShaderParameter("paint_dark",new Color("8d837f"));moonPaint.SetShaderParameter("paint_light",new Color("e2d4bf"));
        moonPaint.SetShaderParameter("globe_kind",2);
        Sphere(_orreryLowMoon,moonAt,Vector3.One*2.6f,moonPaint).Name="CoreMoonGlobe";
        _orreryLowMoon.AddChild(new MeshInstance3D{Name="CoreMoonOrreryArm",
            Mesh=OrreryArmMesh(OrrerySeat(Mathf.Pi*2/3),new(-3.2f,-2.7f,Depth+1.8f),new(-8,-3,12),moonAt,.18f),
            MaterialOverride=_art.Color(new("bfa566"),.48f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        TrackOrreryOrbit(_orreryLowMoon,OrrerySeat(Mathf.Pi*2/3));
        var brass=_art.Toy(new("bfa566"),.48f);
        var meridian=new MeshInstance3D{Name="OrreryCoreMeridian",Mesh=OrreryHoopMesh(),MaterialOverride=brass,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(meridian);
        foreach(float angle in new[]{Mathf.Pi/3,Mathf.Pi*2/3,Mathf.Pi/12})
        {
            var cap=Sphere(this,OrrerySeat(angle),new(.72f,.36f,.72f),brass);cap.Name="MeridianJunctionCap";
        }
        UpdateOrreryCoreQuality();
        var rock=new ShaderMaterial{Shader=new Shader{Code=OrreryCragShader}};
        _castleStone.Add(rock);
        AddChild(new MeshInstance3D{Name="FortressCrag",Mesh=CragMesh(Width,Depth),MaterialOverride=rock});
        var paint=_art.Nebula();paint.SetShaderParameter("stage_center",new Vector2(Width/2f,Depth/2f));
        _ambientMaterials.Add(paint);
        AddChild(new MeshInstance3D{Name="OrreryBackdrop",Mesh=PrimitiveTemplates.Get("BoxMesh",("size",new Vector3(600,.05f,600))),
            MaterialOverride=paint,Position=new(Width/2f,-35,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    /// <summary>A small lacquered astronomical model beside the original keep.
    /// Brass arms support the spheres; no real-sky rings, clouds or debris noise.</summary>
    private void BuildOrbitalBackdrop()
    {
        var scenery=_scenery!;
        var brass=_art.Color(new("bfa566"),.48f);
        var hero=new Vector3(-6.6f,-2.6f,7.1f);
        var companion=new Vector3(Width+3.8f,-3.1f,10.6f);
        var moon=new Vector3(Width+5.2f,-1.9f,4.2f);
        Planet("HeroPlanet",hero,2.6f,new("d4c7b1"),2);
        Planet("CompanionPlanet",companion,1.65f,new("526f85"),1);
        Planet("ToyMoon",moon,.85f,new("8c83a1"),0);
        Arm("HeroOrreryArm",OrrerySeat(Mathf.Pi*2/3),new(-3.2f,-2.7f,Depth+1.8f),new(-8,-3,12),hero);
        Arm("CompanionOrreryArm",OrrerySeat(Mathf.Pi/3),new(Width+3.5f,-2,Depth+2),new(Width+6.5f,-3.4f,14.2f),companion);
        Arm("MoonOrreryArm",OrrerySeat(Mathf.Pi/12),new(Width+6.5f,-1.9f,Depth-2.5f),new(Width+7,-2.6f,7),moon);


        var stars=Prop(scenery,"OrreryStars",Vector3.Zero);
        var starPaint=Glow(new("b9b3d5"),.35f);
        int starOrdinal=0;
        foreach(var(at,scale) in new[]{(new Vector3(-3.1f,-3.2f,-2.5f),.55f),
            (new Vector3(-4.5f,-3.7f,Depth-1.5f),.75f),(new Vector3(Width+4.3f,-3.8f,5.3f),.55f),
            (new Vector3(Width+2.2f,-4.6f,Depth+1.8f),.75f)})
        {
            var star=new MeshInstance3D{Name="FourPointStar"+starOrdinal++,Mesh=OrreryStarMesh(),MaterialOverride=starPaint,
                Position=at,Basis=new Basis(Vector3.Right,new(0,.694f,-.72f),new(0,.72f,.694f)).Scaled(Vector3.One*scale),
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};stars.AddChild(star);
        }
        void Planet(string name,Vector3 at,float diameter,Color color,float phase)
        {
            var root=Prop(scenery,name,at);
            var paint=new ShaderMaterial{Shader=new Shader{Code=OrreryGlobeShader}};
            paint.SetShaderParameter("paint_dark",color.Darkened(.23f));paint.SetShaderParameter("paint_light",color.Lightened(.1f));
            paint.SetShaderParameter("paint_phase",phase);paint.SetShaderParameter("globe_kind",(int)phase);
            _ambientMaterials.Add(paint);
            var globe=Sphere(root,Vector3.Zero,Vector3.One*diameter,paint);globe.Name="Globe";
            globe.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            TrackOrreryOrbit(root,OrrerySeat(name=="HeroPlanet"?Mathf.Pi*2/3:name=="CompanionPlanet"?Mathf.Pi/3:Mathf.Pi/12));
            _sceneryMotion.Add(t=>globe.Basis=PresentationMath.Rotation(new(.07f*PresentationMath.Sin(t*.07f+phase),t*.035f,phase*.1f),Vector3.One*diameter));
        }
        void Arm(string name,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var mesh=new MeshInstance3D{Name=name,Mesh=OrreryArmMesh(a,b,c,d,.18f),MaterialOverride=brass,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};scenery.AddChild(mesh);TrackOrreryOrbit(mesh,a);
        }
    }

    private static ArrayMesh OrreryArmMesh(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float radius)
    {
        const int steps=40,sides=10;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(float t,int side)
        {
            side%=sides;
            float s=1-t;var centre=s*s*s*a+3*s*s*t*b+3*s*t*t*c+t*t*t*d;
            var tangent=(3*s*s*(b-a)+6*s*t*(c-b)+3*t*t*(d-c)).Normalized();
            var right=tangent.Cross(Vector3.Up).Normalized();var up=right.Cross(tangent).Normalized();
            var(sin,cos)=PresentationMath.SinCos(side*Mathf.Tau/sides);
            return centre+(right*cos+up*sin)*radius;
        }
        void Triangle(Vector3 aa,Vector3 bb,Vector3 cc,Vector3 outward)
        {
            var normal=(cc-aa).Cross(bb-aa).Normalized();if(normal.Dot(outward)<0){(bb,cc)=(cc,bb);normal=-normal;}
            foreach(var p in new[]{aa,bb,cc}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
        }
        for(int step=0;step<steps;step++)for(int side=0;side<sides;side++)
        {
            float t=step/(float)steps,next=(step+1f)/steps;var p=Point(t,side);var q=Point(t,side+1);var r=Point(next,side+1);var s=Point(next,side);
            var centre=(Point(t,side)+Point(t,side+sides/2))*.5f;
            Triangle(p,q,r,p-centre);Triangle(p,r,s,p-centre);
        }
        surface.Index();return surface.Commit();
    }

    private static ArrayMesh OrreryStarMesh()
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] outline=[new(0,.6f),new(.13f,.13f),new(.6f,0),new(.13f,-.13f),new(0,-.6f),new(-.13f,-.13f),new(-.6f,0),new(-.13f,.13f)];
        foreach(int sign in new[]{-1,1})for(int i=0;i<outline.Length;i++)
        {
            Vector3 a=new(0,0,sign*.065f),b=new(outline[i].X,outline[i].Y,sign*.065f),c=new(outline[(i+1)%outline.Length].X,outline[(i+1)%outline.Length].Y,sign*.065f);
            var normal=new Vector3(0,0,sign);if((c-a).Cross(b-a).Dot(normal)<0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
        }
        for(int i=0;i<outline.Length;i++)
        {
            var p=outline[i];var q=outline[(i+1)%outline.Length];
            Vector3 a=new(p.X,p.Y,-.065f),b=new(q.X,q.Y,-.065f),c=new(q.X,q.Y,.065f),d=new(p.X,p.Y,.065f);
            var normal=(c-a).Cross(b-a).Normalized();
            if(normal.Dot(new Vector3(p.X+q.X,p.Y+q.Y,0))<0){(b,d)=(d,b);normal=-normal;}
            foreach(var v in new[]{a,b,c,a,c,d}){surface.SetNormal(normal);surface.SetUV(new(v.X,v.Y));surface.AddVertex(v);}
        }
        surface.Index();return surface.Commit();
    }

    private ArrayMesh OrreryHoopMesh()
    {
        const int steps=96,sides=12;const float radius=.18f;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 Point,Vector3 Normal) At(int step,int side)
        {
            step%=steps;side%=sides;
            float angle=step*Mathf.Tau/steps;
            var center=OrrerySeat(angle);
            var tangent=(OrrerySeat(angle+.001f)-OrrerySeat(angle-.001f)).Normalized();
            var outward=Vector3.Up.Cross(tangent).Normalized();
            var(sin,cos)=PresentationMath.SinCos(side*Mathf.Tau/sides);
            var normal=outward*cos+Vector3.Up*sin;
            return(center+normal*radius,normal);
        }
        void Tri((Vector3 Point,Vector3 Normal) a,(Vector3 Point,Vector3 Normal) b,(Vector3 Point,Vector3 Normal) c)
        {
            if((b.Point-a.Point).Cross(c.Point-a.Point).Dot(a.Normal+b.Normal+c.Normal)>=0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetNormal(p.Normal);surface.SetUV(new(p.Point.X,p.Point.Z));surface.AddVertex(p.Point);}
        }
        for(int step=0;step<steps;step++)for(int side=0;side<sides;side++)
        {
            var a=At(step,side);var b=At(step+1,side);var c=At(step+1,side+1);var d=At(step,side+1);
            Tri(a,b,c);Tri(a,c,d);
        }
        surface.Index();return surface.Commit();
    }

    private const string OrreryCragShader="""
shader_type spatial;
render_mode unshaded, fog_disabled;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 world;
varying vec3 painted_normal;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;painted_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
    float cut=world.y+.14*sin(world.x*.35+world.z*.2);
    float strata=cut> -1.65?.85:cut> -2.8?.30:cut> -4.1?.62:.16;
    float brush=smoothstep(-.3,.6,sin(world.x*.23+world.z*.17)+sin(world.z*.34-world.x*.13)*.4);
    float sculpt=.58+.42*max(dot(normalize(painted_normal),normalize(vec3(-.45,.72,.52))),0.0);
    ALBEDO=mix(vec3(.023,.012,.043),vec3(.060,.032,.092),strata)*mix(.88,1.08,brush)*sculpt;
    float rim=pow(1.0-max(dot(NORMAL,VIEW),0.0),3.0);
    EMISSION=vec3(.018,.011,.032)*rim;
    ROUGHNESS=.92;SPECULAR=.16;
}
""";

    private const string OrreryGlobeShader="""
shader_type spatial;
render_mode diffuse_burley, fog_disabled;
uniform vec4 paint_dark : source_color;
uniform vec4 paint_light : source_color;
uniform float paint_phase=0.0;
uniform int globe_kind=0;
uniform float ambient_clock=0.0;
varying vec3 local_sphere;
void vertex(){local_sphere=normalize(VERTEX);}
void fragment(){
    vec3 p=normalize(local_sphere);
    float drift=ambient_clock*.006;
    float wash;
    if(globe_kind==0){
        // One or two broad, soft, irregular painted bands; no fine striping.
        wash=smoothstep(-.25,.55,cos(p.y*4.7+.28*sin(p.x*3.0+p.z*2.0)+drift));
    }else if(globe_kind==1){
        // Large continental washes, avoiding marbled or stippled cloud noise.
        wash=smoothstep(-.35,.4,sin(p.x*3.0+p.z*1.7+drift)*cos(p.y*2.4-p.z));
    }else{
        // Three broad crater variations on the small ivory moon.
        float a=1.0-smoothstep(.13,.28,length(p-vec3(.35,.35,.87)));
        float b=1.0-smoothstep(.14,.32,length(p-vec3(-.45,-.2,.87)));
        float c=1.0-smoothstep(.12,.25,length(p-vec3(.65,-.5,.57)));
        wash=clamp(.82-a*.6-b*.5-c*.45,.05,.9);
    }
    vec3 dark=globe_kind==0?paint_dark.rgb*.82:paint_dark.rgb*.9;
    ALBEDO=mix(dark,paint_light.rgb,wash);
    EMISSION=ALBEDO*.075;
    ROUGHNESS=.46;SPECULAR=.35;CLEARCOAT=.35;CLEARCOAT_ROUGHNESS=.24;
}
""";
}
