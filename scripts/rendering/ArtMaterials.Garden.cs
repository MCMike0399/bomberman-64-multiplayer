using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    public ShaderMaterial GardenSurface(bool grass=false,bool boulder=false)
    {
        var material=new ShaderMaterial{Shader=_gardenShader??=new Shader{Code=GardenShader}};
        GraphicsQuality.ApplyShaderDetail(material);
        material.SetShaderParameter("surface_kind",grass?0:boulder?2:1);
        material.SetShaderParameter("grass_dark",new Color("245a22"));
        material.SetShaderParameter("grass_light",new Color("4c8b32"));
        material.SetShaderParameter("stone_color",new Color(boulder?"77786b":"969780"));
        return material;
    }

    // Original world-space turf and limestone. Derivative filtering keeps the
    // small mineral/grass marks stable at the normal battle camera distance.
    private const string GardenShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform int surface_kind=0;
uniform float quality_detail=1.0;
// High only: wind over the lawn, blade-scale shading and lichen on the stone.
uniform float quality_premium=0.0;
uniform vec4 grass_dark : source_color;
uniform vec4 grass_light : source_color;
uniform vec4 stone_color : source_color;
varying vec3 world;
varying vec3 normal_world;
""" + "\n" + IntegerNoiseHash + "\n" + """
float noise(vec2 p){
 vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
 return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;normal_world=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
 vec2 p=abs(normal_world.y)>.6?world.xz:vec2(abs(normal_world.x)>.6?world.z:world.x,world.y);
 float cloud=noise(p*.72)*.65+noise(p*2.3)*.35;
 float detail=.5;
 if(quality_detail>.5)detail=mix(.5,noise(p*26.0),1.0-smoothstep(.25,1.1,length(fwidth(p*26.0))));
 if(surface_kind==0){
   vec3 turf=mix(grass_dark.rgb,grass_light.rgb,cloud);
   // Lighter tufts and sun-warmed patches give the lawn a hand-painted rhythm.
   float clump=noise(p*1.9)*.6+noise(p*4.3)*.4;
   turf=mix(turf*.84,turf*1.12+vec3(.025,.03,0.0),smoothstep(.36,.76,clump));
   turf=mix(turf,turf*vec3(1.1,1.06,.8),smoothstep(.55,.8,noise(p*.31))*.45);
   ALBEDO=turf*(.96+detail*.08);
   if(quality_premium>.5){
     // Wind over the lawn: slow bright bands cross the turf as gusts bend the blades,
     // and a blade-scale ripple tilts the normal so the sun textures the grass.
     float gust=noise(p*.38+vec2(TIME*.31,TIME*.09))*.6+noise(p*.85-vec2(TIME*.17,-TIME*.23))*.4;
     ALBEDO*=mix(.9,1.12,smoothstep(.36,.68,gust));
     float blade=mix(.5,noise(p*8.0)*.6+noise(p*17.0)*.4,1.0-smoothstep(.3,1.0,length(fwidth(p*17.0))));
     vec3 dpdx=dFdx(VERTEX),dpdy=dFdy(VERTEX);
     vec3 r1=cross(dpdy,NORMAL),r2=cross(NORMAL,dpdx);
     float determinant=dot(dpdx,r1);
     vec3 gradient=(r1*dFdx(blade)+r2*dFdy(blade))*sign(determinant)/max(abs(determinant),.00001);
     NORMAL=normalize(NORMAL-gradient*.03);
   }
   if(quality_detail>.5&&normal_world.y>.6){
     // Sparse tiny flowers; they fade before becoming subpixel noise.
     vec2 fp=p*2.2,cell=floor(fp);
     float h=hash(cell+7.7);
     float fade=1.0-smoothstep(.12,.4,fwidth(fp.x));
     if(h>.968&&fade>.01){
       vec2 centre=vec2(hash(cell+1.3),hash(cell+9.1))*.6+.2;
       float d=length(fract(fp)-centre),aa=fwidth(fp.x);
       vec3 petal=h>.99?vec3(.98,.97,.92):h>.98?vec3(1.0,.9,.42):vec3(1.0,.68,.8);
       ALBEDO=mix(ALBEDO,petal,(1.0-smoothstep(.085-aa,.085+aa,d))*fade*.85);
       ALBEDO=mix(ALBEDO,vec3(1.0,.82,.3),(1.0-smoothstep(.035-aa,.035+aa,d))*fade);
     }
   }
   ROUGHNESS=.96;
 }else{
   vec3 base=stone_color.rgb;
   ALBEDO=base*(.68+cloud*.43+(detail-.5)*.10);
   // Fine discontinuous vertical fissures, plus moss close to the ground.
   if(quality_detail>.5){
   float vein=abs(sin(p.x*8.0+noise(p*1.1)*3.0));
   float aa=max(fwidth(vein),.01);
   float crack=1.0-smoothstep(.025-aa,.025+aa,vein);
   if(crack>0.0)crack*=smoothstep(.5,.72,noise(p*1.5));
   ALBEDO*=1.0-crack*.5;
   }
   // Moss is exactly zero from .25 upward; skip its hash there.
   float moss=world.y<.25?(1.0-abs(normal_world.y))*(1.0-smoothstep(.03,.25,world.y))*smoothstep(.35,.72,noise(world.xz*6.0)):0.0;
   ALBEDO=mix(ALBEDO,vec3(.19,.28,.10),moss*.5);
   if(quality_premium>.5){
     // Lichen rosettes: pale sage patches with ragged rims on a few weathered faces.
     vec2 lp=p*1.1,lcell=floor(lp);float lh=hash(lcell+3.3);
     float lfade=1.0-smoothstep(.08,.3,length(fwidth(lp)));
     if(lh>.9&&lfade>.01){
       vec2 centre=vec2(hash(lcell+1.7),hash(lcell+8.2))*.5+.25;
       float d=length(fract(lp)-centre)+(noise(p*11.0)-.5)*.1;
       ALBEDO=mix(ALBEDO,vec3(.55,.61,.42),(1.0-smoothstep(.08,.16,d))*.36*lfade);
     }
   }
   // Walls and columns deepen toward the turf, like soft bounce shadow in a diorama.
   ALBEDO*=mix(1.0,mix(.8,1.0,smoothstep(-.2,1.7,world.y)),1.0-abs(normal_world.y));
   ROUGHNESS=.9;
 }
}
""";
}
