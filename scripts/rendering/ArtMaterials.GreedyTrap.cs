using Godot;

namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    /// <summary>Original world-space textures; no extracted game images or external assets.</summary>
    public ShaderMaterial GreedyTrap(int style)
    {
        var material=new ShaderMaterial{Shader=_greedyTrapShader??=new Shader{Code=GreedyTrapShader}};
        material.SetShaderParameter("surface_style",style);
        GraphicsQuality.ApplyShaderDetail(material);
        return material;
    }

    private const string GreedyTrapShader="""

shader_type spatial;
render_mode diffuse_burley;
uniform int surface_style=0;
uniform float quality_detail=1.0;
// High only: joints with depth, cavity shading toward them and a glaze on the pool tiles.
uniform float quality_premium=0.0;
uniform float compression=0.0;
varying vec3 world;
varying vec3 world_normal;
""" + "\n" + IntegerNoiseHash + "\n" + """
vec2 hash2(vec2 p){return vec2(hash(p),hash(p+vec2(43.7,17.2)));}
float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
// Joint relief of each surface style at a surface coordinate: x is the bevel
// (0 in a joint, 1 on the face), y the raw joint distance; owner is the stone's cell.
vec2 relief_at(vec2 pp,out vec2 owner){
    owner=vec2(0);
    if(surface_style==0){
        // Broad irregular court cobbles with quiet per-stone warm/cool variation.
        vec2 q=pp*1.55,cell=floor(q),f=fract(q);
        float first=9.0,second=9.0;
        for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
            vec2 o=vec2(float(x),float(y));float d=length(o+.23+hash2(cell+o)*.54-f);
            if(d<first){second=first;first=d;owner=cell+o;}else second=min(second,d);
        }
        float edge=second-first,aa=max(fwidth(edge),.008);
        return vec2(smoothstep(.025-aa,.075+aa,edge),edge);
    }else if(surface_style==1||surface_style==3){
        // Staggered fired burgundy brick outside; mossy gray ashlar inside.
        vec2 q=pp*(surface_style==1?vec2(1.5,3.6):vec2(2.5,4.2));
        q.x+=mod(floor(q.y),2.0)*.5;
        vec2 e=min(fract(q),1.0-fract(q));float d=min(e.x,e.y);
        float aa=max(fwidth(d),.006);owner=floor(q);
        return vec2(smoothstep(.025-aa,.065+aa,d),d);
    }else if(surface_style==2){
        vec2 e=min(fract(pp),1.0-fract(pp));float d=min(e.x,e.y);owner=floor(pp);
        return vec2(smoothstep(.018,.055+fwidth(d),d),d);
    }else if(surface_style==4){
        vec2 e=min(fract(pp),1.0-fract(pp));float d=min(e.x,e.y);owner=floor(pp);
        return vec2(smoothstep(.015,.045+fwidth(d),d),d);
    }
    return vec2(0.0);
}
void fragment(){
    bool top=abs(world_normal.y)>.6;
    vec2 p=top?world.xz:vec2(abs(world_normal.x)>.6?world.z:world.x,world.y);
    float grain=0.0;
    if(quality_detail>.5)grain=(noise(p*49.0)-.5)*.026*(1.0-smoothstep(.25,.8,length(fwidth(p*49.0))));
    vec3 paint=vec3(.5);float shine=.2;float rough=.82;
    vec2 owner;vec2 joint=relief_at(p,owner);float relief=joint.x;
    if(surface_style==0){
        vec3 stone=mix(vec3(.47,.34,.23),vec3(.72,.60,.43),hash(owner));
        stone=mix(stone,vec3(.52,.52,.43),noise(p*.7)*.17);
        paint=mix(vec3(.26,.205,.155),stone,relief);
    }else if(surface_style==1||surface_style==3){
        float variation=hash(owner);
        vec3 brick=surface_style==1?mix(vec3(.20,.045,.068),vec3(.40,.105,.13),variation):mix(vec3(.25,.35,.33),vec3(.48,.59,.53),variation);
        paint=mix(surface_style==1?vec3(.09,.035,.041):vec3(.13,.22,.21),brick,relief);
        paint*=mix(.77,1.0,smoothstep(-.2,1.5,world.y));
    }else if(surface_style==2){
        // Glazed teal flagstones, emphatically opaque: this chamber is a floor.
        vec3 tile=mix(vec3(.075,.29,.31),vec3(.19,.46,.46),hash(owner));
        tile+=vec3(.028,.036,.025)*(noise(p*4.0)-.5);
        paint=mix(vec3(.07,.16,.17),tile,relief);rough=.42;shine=.4;
    }else if(surface_style==4){
        paint=mix(vec3(.33,.34,.29),mix(vec3(.62,.61,.49),vec3(.77,.76,.64),hash(owner)),relief);
    }else{
        // Amber machine inlays breathe during compression; never mark a safe route.
        paint=vec3(.67,.39,.12);rough=.38;shine=.5;
        float pulse=.5+.5*sin(TIME*5.5-world.z*.7);
        EMISSION=vec3(.58,.18,.025)*compression*(.15+.3*pulse);
    }
    if(quality_premium>.5&&surface_style<5){
        // High: joints have depth. A view ray reaching a joint's floor under the
        // neighbouring stone met that stone's side wall instead, and each face
        // darkens slightly toward its joints like a cavity. The pool tiles get a glaze.
        vec3 view_world=normalize((INV_VIEW_MATRIX*vec4(VIEW,0.0)).xyz);
        vec2 view_plane=top?view_world.xz:(abs(world_normal.x)>.6?view_world.zy:view_world.xy);
        vec2 floor_owner;
        vec2 floor_joint=relief_at(p-view_plane*(.008/max(abs(dot(view_world,world_normal)),.25)),floor_owner);
        paint=mix(paint,paint*.55,(1.0-relief)*floor_joint.x);
        paint*=mix(.82,1.0,smoothstep(0.0,.14,joint.y));
        if(surface_style==2){CLEARCOAT=.6;CLEARCOAT_ROUGHNESS=.12;}
    }
    ALBEDO=paint+grain;ROUGHNESS=rough;SPECULAR=shine;
    if(quality_detail>.5){NORMAL_MAP=vec3(.5+dFdx(relief)*.12,.5-dFdy(relief)*.12,1.0);NORMAL_MAP_DEPTH=.24;}
}
""";
}
