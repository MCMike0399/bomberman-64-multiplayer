using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Arena.Effects;

/// <summary>Toon fireball after the original's star-cored sphere: a white-hot pop, banded boiling body,
/// camera-facing star, jetting sparks, brief charged flares and a distinct power ring, then a burning
/// dissolve with small puffs. Every layer fades in its shader, so one warmed pipeline serves each age.
/// The faint mapped danger shell retains authority extent; the central fireball stays round in world space.</summary>
public partial class ExplosionView : Node3D
{
    private const string Noise="""
float hash3(vec3 p){p=fract(p*.3183099+.1);p*=17.0;return fract(p.x*p.y*p.z*(p.x+p.y+p.z));}
float noise3(vec3 x){vec3 i=floor(x);vec3 f=fract(x);f=f*f*(3.0-2.0*f);
 return mix(mix(mix(hash3(i),hash3(i+vec3(1,0,0)),f.x),mix(hash3(i+vec3(0,1,0)),hash3(i+vec3(1,1,0)),f.x),f.y),
  mix(mix(hash3(i+vec3(0,0,1)),hash3(i+vec3(1,0,1)),f.x),mix(hash3(i+vec3(0,1,1)),hash3(i+vec3(1,1,1)),f.x),f.y),f.z);}
float fbm3(vec3 p){return noise3(p)*.58+noise3(p*2.03+1.7)*.29+noise3(p*4.11+3.1)*.13;}
""";
    // Banded heat: crisp toon steps from ember rim to white-hot center, then a burning-edge dissolve.
    private static readonly Shader FireShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;
uniform float age=0.0;
uniform float duration=.7;
uniform float seed=0.0;
uniform float lumps=.08;
uniform bool shell=false;
uniform bool blue=false;
varying vec3 dir;
""" + Noise + """
float band(float h,float at){return smoothstep(at-.035,at+.035,h);}
void vertex(){
 dir=normalize(VERTEX);
 // Low-frequency billows rise through the body; a lumpy silhouette instead of a smooth ball.
 float billow=fbm3(dir*2.4+vec3(seed,seed*.7-age*2.6,seed*1.3));
 if(!shell)VERTEX+=NORMAL*(billow-.45)*lumps;
}
void fragment(){
 float facing=clamp(dot(normalize(NORMAL),normalize(VIEW)),0.0,1.0);
 float rim=1.0-facing;
 if(shell){ALBEDO=blue?vec3(.02,.74,.76):vec3(1.0,.23,.015);ALPHA=pow(rim,1.6)*.10*(1.0-smoothstep(.32,.4,age));}
 else{
  float boil=fbm3(dir*3.3+vec3(seed*1.9,-age*3.4,age*1.7+seed));
  float flash=1.0-smoothstep(.02,.10,age);
  float cool=smoothstep(.08,.5,age);
  float heat=facing*1.05+(boil-.5)*.95+flash*1.1-cool*.62;
  vec3 ember=blue?vec3(0.0,.22,.30):vec3(.62,.05,.01);
  vec3 body=blue?vec3(.03,.62,.66):vec3(1.0,.30,.02);
  vec3 bright=blue?vec3(.42,1.0,.90):vec3(1.0,.72,.10);
  vec3 hot=blue?vec3(1.25,1.45,1.40):vec3(1.55,1.40,.95);
  vec3 col=mix(ember,body,band(heat,.26));
  col=mix(col,bright,band(heat,.62));
  col=mix(col,hot,band(heat,.98));
  // Vapor: toward translucent amber/teal smoke while the dissolve eats holes from the noise.
  float vapor=smoothstep(.34,duration,age);
  vec3 smoke=blue?vec3(.10,.48,.50):vec3(.42,.20,.08);
  col=mix(col,smoke,vapor*.7);
  float erosion=fbm3(dir*4.6+vec3(seed*3.1,-age*1.2,0.0))+rim*.18;
  float cut=mix(-.2,1.05,smoothstep(.36,duration*.98,age));
  float burn=1.0-smoothstep(cut,cut+.09,erosion);
  col=mix(col,blue?vec3(.7,1.3,1.2):vec3(1.5,.55,.08),burn*vapor);
  ALBEDO=col;
  ALPHA=smoothstep(cut-.02,cut+.02,erosion)*(.94-vapor*.36)*(1.0-smoothstep(.9,1.0,age/duration));
 }
}
"""};
    // Camera-facing star burst: the original's identity mark, pulled forward so terrain cannot clip it.
    private static readonly Shader StarShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, skip_vertex_transform;
uniform float age=0.0;
uniform float spin=0.0;
uniform float points=5.0;
uniform float pull=.6;
uniform float life=.32;
uniform bool blue=false;
void vertex(){
 float s=length(MODEL_MATRIX[0].xyz);
 vec3 center=(VIEW_MATRIX*vec4(MODEL_MATRIX[3].xyz,1.0)).xyz;
 center.z+=pull*s;
 float c=cos(spin),n=sin(spin);
 VERTEX=center+vec3(mat2(vec2(c,n),vec2(-n,c))*VERTEX.xy*s,0.0);
 NORMAL=vec3(0,0,1);
}
void fragment(){
 vec2 p=(UV-.5)*2.0;
 float r=length(p),a=atan(p.y,p.x);
 float k=abs(fract(a*points/6.28318+.25)-.5)*2.0;
 float edge=mix(.46,.95,pow(1.0-k,1.7));
 float star=1.0-smoothstep(edge-.03,edge+.02,r);
 float core=1.0-smoothstep(.18,.42,r);
 float halo=(1.0-smoothstep(.0,1.0,r))*.35;
 vec3 fill=blue?vec3(.85,1.15,1.1):vec3(1.35,1.05,.35);
 vec3 white=vec3(1.6,1.55,1.4);
 ALBEDO=mix(fill,white,core);
 float fade=1.0-smoothstep(life*.55,life,age);
 ALPHA=clamp(max(star*.95,halo),0.0,1.0)*fade;
}
"""};
    // Hot white tips cooling to orange: brief charged flares and jetting sparks share this ramp.
    private static readonly Shader StreakShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform float age=0.0;
uniform float life=.28;
uniform bool blue=false;
uniform bool sparks=false;
varying float heat;
varying float alive;
void vertex(){heat=sparks?INSTANCE_CUSTOM.r:1.0-UV.y;alive=sparks?INSTANCE_CUSTOM.g:1.0;}
void fragment(){
 float t=sparks?1.0-heat:clamp(age/life,0.0,1.0);
 // Sparks burn white to orange; flares stay saturated so the white star keeps the center.
 vec3 hot=blue?vec3(1.2,1.5,1.45):sparks?vec3(1.6,1.35,.7):vec3(1.45,1.0,.22);
 vec3 warm=blue?vec3(.1,.85,.9):vec3(1.0,.36,.03);
 ALBEDO=mix(hot,warm,smoothstep(.15,.85,sparks?t:UV.y*.8+t*.4));
 ALPHA=alive*(sparks?1.0:1.0-smoothstep(.55,1.0,t));
}
"""};
    // Pressure rim: a fresnel-only shell that outruns the body for a few frames.
    private static readonly Shader ShockShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;
uniform float age=0.0;
uniform bool blue=false;
void fragment(){
 float rim=1.0-clamp(dot(normalize(NORMAL),normalize(VIEW)),0.0,1.0);
 float t=clamp(age/.2,0.0,1.0);
 ALBEDO=blue?vec3(.8,1.2,1.2):vec3(1.3,1.15,.85);
 ALPHA=pow(rim,3.0)*.55*(1.0-t)*(1.0-t);
}
"""};
    // Power ground rim: a thin double ring with a faint fill, as the original red bomb shows.
    private static readonly Shader RingShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform float age=0.0;
void fragment(){
 float d=length(UV-.5)*2.0;
 float outer=smoothstep(.86,.92,d)*(1.0-smoothstep(.95,.995,d));
 float inner=smoothstep(.70,.74,d)*(1.0-smoothstep(.76,.80,d))*.55;
 float fill=smoothstep(.15,.9,d)*(1.0-smoothstep(.9,.95,d))*.16;
 ALBEDO=mix(vec3(.35,1.0,.95),vec3(1.2,1.4,1.4),outer);
 ALPHA=(outer+inner+fill)*(1.0-smoothstep(.16,.42,age));
}
"""};
    // Faceted toon puffs: smoke revealed as the fire burns away, or the power blast's white rim puffs.
    private static readonly Shader PuffShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;
uniform bool blue=false;
varying vec3 local;
varying float fade;
varying float id;
""" + Noise + """
void vertex(){local=VERTEX;fade=INSTANCE_CUSTOM.r;id=float(INSTANCE_ID);}
void fragment(){
 vec3 n=normalize((INV_VIEW_MATRIX*vec4(NORMAL,0.0)).xyz);
 float lit=step(.15,dot(n,normalize(vec3(-.35,1.0,.25))));
 vec3 shade=blue?vec3(.62,.86,.86):vec3(.045,.032,.028);
 vec3 light=blue?vec3(1.0,1.08,1.08):vec3(.15,.10,.075);
 ALBEDO=mix(shade,light,lit);
 float grain=noise3(local*3.2+vec3(id*3.7));
 ALPHA=smoothstep(fade-.04,fade+.04,grain)*.82;
}
"""};
    private static readonly SphereMesh BodyMesh=new() {Radius=.5f,Height=1,RadialSegments=32,Rings=16};
    private static readonly SphereMesh ShockMesh=new() {Radius=.5f,Height=1,RadialSegments=24,Rings=12};
    private static readonly QuadMesh StarMesh=new() {Size=Vector2.One};
    private static readonly PlaneMesh RingMesh=new() {Size=Vector2.One};
    private static readonly CylinderMesh FlareMesh=new() {TopRadius=0,BottomRadius=.19f,Height=1,RadialSegments=3,Rings=1};
    private static readonly SphereMesh SparkMesh=new() {Radius=.5f,Height=1,RadialSegments=4,Rings=2};
    private static readonly SphereMesh PuffMesh=new() {Radius=.5f,Height=1,RadialSegments=8,Rings=4};
    private static int _serial;
    private readonly ShaderMaterial _fire,_boundary,_star,_streak,_sparkFire,_shockFire,_ring,_puffFire;
    private readonly ShaderMaterial[] _aged;
    private readonly MeshInstance3D _body,_starBurst,_shock,_wave,_shell;
    private readonly MultiMeshInstance3D _flareSet,_sparkSet,_puffSet;
    private readonly OmniLight3D? _light;
    private readonly List<Vector3> _flares=[];
    private readonly List<(Vector3 Direction,float Speed,float Life)> _sparks=[];
    private readonly List<(Vector3 Direction,float Delay)> _puffs=[];
    private readonly float _radius,_duration,_starLife,_spin;
    private readonly bool _power;
    private float _age;
    public string Tier {get;}
    public float Radius=>_radius;
    public float Age=>_age;
    public bool HazardActive=>_age<.4f;
    public string Phase=>_age<.12f?"flash":_age<.4f?"bloom":"vapor";
    public float VisualExtent {get;private set;}
    public float BodyAspect
    {
        get {var basis=_body.GlobalBasis;var s=new Vector3(basis.X.Length(),basis.Y.Length(),basis.Z.Length());return Math.Max(s.X,Math.Max(s.Y,s.Z))/Math.Max(.00001f,Math.Min(s.X,Math.Min(s.Y,s.Z)));}
    }
    /// <summary>Renderer instances owned by this effect; particle layers are single MultiMesh draws.</summary>
    public int GeometryCount=>FindChildren("*","GeometryInstance3D",true,false).Count;
    public int FlareCount=>_flares.Count;
    public int SparkCount=>_sparks.Count;
    public bool PowerWaveVisible=>_wave.Visible;
    public ExplosionView(ArtMaterials art,float radius,bool red,float initialAge=0,bool reducedDetail=false)
    {
        _radius=radius;_age=initialAge;_power=red;
        Tier=radius<=1.6f?"small":radius<=2.7f?"medium":"large";
        _duration=Tier=="small"?.52f:Tier=="medium"?.68f:.82f;
        _starLife=red?.46f:.32f;
        int serial=_serial++;float seed=(serial*.618034f)%1*17;
        bool lighter=reducedDetail||GraphicsQuality.Current==GraphicsPreset.Low;
        _fire=Material(FireShader,red,("duration",_duration),("seed",seed),("lumps",.06f+radius*.012f));
        _boundary=Material(FireShader,red,("shell",true));
        _spin=seed;_star=Material(StarShader,red,("life",_starLife),("spin",seed));_star.RenderPriority=2;
        _streak=Material(StreakShader,red);_streak.RenderPriority=1;
        _sparkFire=Material(StreakShader,red,("sparks",true));_sparkFire.RenderPriority=3;
        _shockFire=Material(ShockShader,red);
        _ring=Material(RingShader,red);
        _puffFire=Material(PuffShader,red);_puffFire.RenderPriority=-1;
        _aged=[_fire,_boundary,_star,_streak,_shockFire,_ring];
        _shell=Sphere(this,Vector3.Zero,Vector3.One*radius*2,_boundary);
        // Terrain maps authority Y to render Y. Compensate only the cosmetic body,
        // retaining that mapping on the full-radius danger shell and effect origin.
        var fireball=new Node3D {Scale=new(1,1/TempleStageView.HeightScale,1)};AddChild(fireball);
        _body=Mesh(fireball,BodyMesh,_fire);
        // The billboard moves toward the camera in its shader, outside the quad's own bounds.
        _starBurst=Mesh(fireball,StarMesh,_star);_starBurst.ExtraCullMargin=radius*2;
        _shock=Mesh(fireball,ShockMesh,_shockFire);
        _wave=Mesh(fireball,RingMesh,_ring);_wave.Visible=red;
        int flares=Tier=="small"?0:reducedDetail?4:Tier=="medium"?6:10;
        for(int i=0;i<flares;i++)
        {
            float angle=(i+(i%2)*.35f)*Mathf.Tau/flares;
            _flares.Add(new Vector3(Mathf.Cos(angle),.12f+(i%3)*.27f,Mathf.Sin(angle)).Normalized());
        }
        // Sparks leave in a few directional jets rather than an even, floaty spray.
        int jets=lighter?3:Tier=="small"?4:5,perJet=lighter?2:Tier=="large"?5:4;
        for(int j=0;j<jets;j++)
        {
            float yaw=(j+Hash(serial*7+j)*.6f)*Mathf.Tau/jets,lift=.25f+Hash(serial*13+j)*.55f;
            var jet=new Vector3(Mathf.Cos(yaw),lift,Mathf.Sin(yaw)).Normalized();
            for(int k=0;k<perJet;k++)
            {
                int h=serial*31+j*7+k;
                var spread=new Vector3(Hash(h)-.5f,Hash(h+3)-.5f,Hash(h+5)-.5f)*.55f;
                _sparks.Add(((jet+spread).Normalized(),.9f+Hash(h+9)*.7f,.22f+Hash(h+11)*.2f));
            }
        }
        int puffs=lighter?3:Tier=="small"?4:7;
        for(int i=0;i<puffs;i++)
        {
            float yaw=(i+Hash(serial*17+i)*.5f)*Mathf.Tau/puffs;
            float lift=red?.05f+Hash(serial*19+i)*.3f:.45f+Hash(serial*19+i)*.6f;
            _puffs.Add((new Vector3(Mathf.Cos(yaw),lift,Mathf.Sin(yaw)).Normalized(),Hash(serial*23+i)*.06f));
        }
        _flareSet=Particles(fireball,FlareMesh,_streak,_flares.Count);
        _sparkSet=Particles(fireball,SparkMesh,_sparkFire,_sparks.Count);
        _puffSet=Particles(fireball,PuffMesh,_puffFire,_puffs.Count);
        if(!lighter)
        {
            _light=new OmniLight3D {LightColor=red?new Color(.45f,1,.95f):new Color(1,.62f,.28f),OmniRange=radius*2.6f,
                OmniAttenuation=1.6f,ShadowEnabled=false,LightSpecular=.2f};
            fireball.AddChild(_light);
        }
        _shell.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        foreach(var mesh in fireball.GetChildren().OfType<GeometryInstance3D>())mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        Scale=new(1,TempleStageView.HeightScale,1);
        Render();
    }
    private static float Hash(int n)=>Mathf.PosMod(Mathf.Sin(n*12.9898f+78.233f)*43758.547f,1);
    private static ShaderMaterial Material(Shader shader,bool blue,params (string Name,Variant Value)[] values)
    {
        var material=new ShaderMaterial {Shader=shader};material.SetShaderParameter("blue",blue);
        foreach(var (name,value) in values)material.SetShaderParameter(name,value);
        return material;
    }
    private static MeshInstance3D Mesh(Node3D parent,Mesh mesh,Material material)
    {var node=new MeshInstance3D {Mesh=mesh,MaterialOverride=material};parent.AddChild(node);return node;}
    private static MultiMeshInstance3D Particles(Node3D parent,Mesh mesh,Material material,int count)
    {
        var set=new MultiMeshInstance3D {Multimesh=new MultiMesh {TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseCustomData=true,Mesh=mesh,InstanceCount=count},
            MaterialOverride=material,Visible=count>0};
        parent.AddChild(set);return set;
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        if(_age>=_duration){QueueFree();return;}
        Render();
    }
    private void Render()
    {
        // Damped spring: a quick pop just past the bomb-sized ignition, settling at full extent.
        float a=_age,spring=1-Mathf.Exp(-24*a)*(Mathf.Cos(24*a)+Mathf.Sin(24*a));
        float growth=Mathf.Clamp(spring,0,1.06f),tail=Mathf.Clamp((a-.4f)/(_duration-.4f),0,1);
        foreach(var material in _aged)material.SetShaderParameter("age",a);
        _shell.Visible=HazardActive;
        VisualExtent=_radius*(.35f+.65f*growth);
        _body.Scale=Vector3.One*2*VisualExtent*(1+tail*.1f);
        _starBurst.Visible=a<_starLife;
        float pop=Mathf.Clamp(a/.05f,0,1),settle=Mathf.Clamp((a-.05f)/(_starLife-.05f),0,1);
        _starBurst.Scale=Vector3.One*_radius*(.4f+1.3f*pop-.45f*settle);
        _star.SetShaderParameter("spin",_spin+a*(_power?1.6f:2.4f));
        _shock.Visible=a<.2f;
        _shock.Scale=Vector3.One*2*_radius*(.6f+.75f*(1-Mathf.Exp(-a*16)));
        _wave.Visible=_power&&a<.42f;
        _wave.Position=new(0,-.27f,0);
        // The original rim sweeps well past the body before it fades.
        _wave.Scale=new Vector3(2,1,2)*_radius*(.3f+1.5f*(1-Mathf.Exp(-a*9)));
        if(_light!=null)
        {
            _light.Visible=a<.35f;
            _light.LightEnergy=3.2f*Mathf.Clamp(a/.025f,0,1)*Mathf.Exp(-a*11);
        }
        var flares=_flareSet.Multimesh;
        float flareGrowth=Mathf.Sin(Mathf.Clamp(a/.28f,0,1)*Mathf.Pi);
        _flareSet.Visible=_flares.Count>0&&a<.28f;
        for(int i=0;i<_flares.Count;i++)
        {
            var direction=_flares[i];float length=1+(i%3)*.22f;
            var basis=new Basis(new Quaternion(Vector3.Up,direction)).Scaled(new Vector3(.8f,.75f*length,.8f)*_radius*Math.Max(.001f,flareGrowth));
            flares.SetInstanceTransform(i,new Transform3D(basis,direction*_radius*(.7f+.2f*growth)));
        }
        var sparks=_sparkSet.Multimesh;
        _sparkSet.Visible=_sparks.Count>0&&a<.45f;
        for(int i=0;i<_sparks.Count;i++)
        {
            var (direction,speed,life)=_sparks[i];
            float t=Mathf.Clamp(a/life,0,1),drag=1-Mathf.Exp(-a*7);
            // Heavy drag with a little gravity: sparks streak out, slow, droop and burn out.
            var position=direction*_radius*(.35f+1.05f*speed*drag)+Vector3.Down*_radius*.9f*a*a;
            var velocity=direction*_radius*speed*7*Mathf.Exp(-a*7)+Vector3.Down*_radius*1.8f*a;
            float stretch=Mathf.Clamp(velocity.Length()*.07f,.15f,_radius*.45f),width=_radius*.06f*(1-t*.6f);
            var forward=velocity.LengthSquared()>.0001f?velocity.Normalized():direction;
            var basis=new Basis(new Quaternion(Vector3.Back,forward)).Scaled(new Vector3(width,width,stretch));
            sparks.SetInstanceTransform(i,new Transform3D(t<1?basis:basis.Scaled(Vector3.Zero),position));
            sparks.SetInstanceCustomData(i,new Color(1-t,t<1?1:0,0,0));
        }
        var puffs=_puffSet.Multimesh;
        // Power puffs pop just outside the rim; ordinary smoke sits inside, behind the burning body.
        const float PuffStart=.26f;
        _puffSet.Visible=_puffs.Count>0&&a>=PuffStart;
        for(int i=0;i<_puffs.Count;i++)
        {
            var (direction,delay)=_puffs[i];
            float t=Mathf.Clamp((a-PuffStart-delay)/(_duration-PuffStart-delay),0,1);
            float size=_radius*(_power?.24f:.42f)*Mathf.Clamp(t*4,0,1)*(1+t*.4f);
            var position=direction*_radius*(_power?.98f+.2f*t:.5f+.15f*t)+Vector3.Up*_radius*.3f*t;
            puffs.SetInstanceTransform(i,new Transform3D(Basis.Identity.Scaled(Vector3.One*Math.Max(.0001f,size)),position));
            puffs.SetInstanceCustomData(i,new Color(Mathf.Lerp(-.1f,1.05f,Mathf.Pow(t,1.6f)),0,0,0));
        }
    }
}
