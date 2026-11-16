using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private StandardMaterial3D? _workshopLampPaint;
    private ShaderMaterial? _workshopLampGlow;
    private Node3D? _workshopRaft;
    private float _workshopClock;

    private void BuildCircuitWorkshop()
    {
        var enamel=_art.Toy(new("c6c6b3"),.84f);var teal=_art.Toy(new("397d78"),.76f);
        var cream=_art.Toy(new("ded9be"),.85f);var brass=_art.Toy(new("b39a62"),.72f);
        // Rounded bodies stay below exact support planes, never forming a new
        // walkable canal crossing. The pale worktop replaces the tiled sage slab.
        RoundedBox(this,new(9.5f,-.94f,8.5f),new(19.4f,1.82f,17.4f),enamel,.15f).Name="WorkshopMainPlinth";
        RoundedBox(this,new(21.5f,-.94f,12),new(3.35f,1.82f,6.35f),enamel,.15f).Name="WorkshopLandingPlinth";
        RoundedBox(this,new(11.5f,.6675f,5),new(7.03f,1.335f,4.03f),teal,.065f).Name="WorkshopTeachingTerrace";
        RoundedBox(this,new(11.5f,1.3425f,2),new(3.03f,2.685f,2.03f),teal,.06f).Name="WorkshopHighTargetBody";
        foreach(float z in new[]{.04f,16.96f})
            Box(this,new(9.5f,-.22f,z),new(18.8f,.12f,.07f),teal,.025f).Name="WorkshopPlinthInlay";
        var paint=new ShaderMaterial{Shader=new Shader{Code=WorkshopSurfaceShader}};
        GraphicsQuality.ApplyShaderDetail(paint);_gardenMaterials.Add(paint);
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);if(cell.Kind is CellKind.Water or CellKind.Void)continue;
            float y0=cell.Tier*HeightScale,y1=y0;
            if(cell.Kind==CellKind.Ramp)
            {
                float from=cell.RampFromTier*HeightScale,to=cell.RampToTier*HeightScale;
                y0=cell.RampDZ<0?to:from;y1=cell.RampDZ<0?from:to;
                var cheek=new SurfaceTool();cheek.Begin(Mesh.PrimitiveType.Triangles);
                foreach(float xx in new[]{(float)x,x+1f})
                    WorkshopQuad(cheek,new(xx,0,z),new(xx,y0,z),new(xx,y1,z+1),new(xx,0,z+1),xx<x+.5f?Vector3.Left:Vector3.Right);
                cheek.Index();AddChild(new MeshInstance3D{Name=$"WorkshopRampBody{x}_{z}",Mesh=cheek.Commit(),MaterialOverride=teal});
            }
            WorkshopQuad(surface,new(x,y0,z),new(x+1,y0,z),new(x+1,y1,z+1),new(x,y1,z+1),Vector3.Up);
            if(cell.Kind==CellKind.Pillar)BuildWorkshopPillar(x,z,teal,cream,brass);
        }
        surface.Index();AddChild(new MeshInstance3D{Name="WorkshopMeasuredWorktop",Mesh=surface.Commit(),MaterialOverride=paint});
        BuildWorkshopTargetRaft();
    }

    private void BuildWorkshopPillar(int x,int z,Material teal,Material cream,Material brass)
    {
        var root=new Node3D{Name=$"WorkshopFlagPost{x}_{z}",Position=new(x+.5f,0,z+.5f)};AddChild(root);
        RoundedBox(root,new(0,.095f,0),new(.94f,.19f,.94f),cream,.22f);
        RoundedBox(root,new(0,.6f,0),new(.67f,.96f,.67f),teal,.12f);
        RoundedBox(root,new(0,1.13f,0),new(.9f,.16f,.9f),cream,.22f);
        Box(root,new(0,1.65f,0),new(.065f,1.4f,.065f),brass,.018f);
        Box(root,new(.28f,2.05f,0),new(.55f,.5f,.035f),teal,.025f);
        Box(root,new(.28f,2.05f,.026f),new(.25f,.05f,.015f),cream);
        _workshopLampPaint??=new StandardMaterial3D{AlbedoColor=new("e4b86d"),EmissionEnabled=true,
            Emission=new("ffd08a"),EmissionEnergyMultiplier=.05f,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,DisableFog=true};
        Bead(root,new(0,2.43f,0),new(.24f,.27f,.24f),_workshopLampPaint).Name="WorkshopFlagLamp";
        _workshopLampGlow??=new ShaderMaterial{Shader=new Shader{Code=WorkshopLampGlowShader}};
        var halo=new SurfaceTool();halo.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] points=[new(-.65f,-.65f,0),new(.65f,-.65f,0),new(.65f,.65f,0),new(-.65f,.65f,0)];
        foreach(int index in new[]{0,2,1,0,3,2})
        {var p=points[index];halo.SetNormal(Vector3.Back);halo.SetUV(new(p.X/1.3f+.5f,p.Y/1.3f+.5f));halo.AddVertex(p);}
        halo.Index();root.AddChild(new MeshInstance3D{Name="WorkshopFlagLampHalo",Position=new(0,2.43f,0),Mesh=halo.Commit(),MaterialOverride=_workshopLampGlow,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private void ApplyWorkshopLighting(StageLighting lighting)
    {
        bool night=lighting.Label=="Night";
        if(_workshopLampPaint is not null)_workshopLampPaint.EmissionEnergyMultiplier=night?1.15f:.05f;
        _workshopLampGlow?.SetShaderParameter("lamp_strength",night?1f:0f);
    }

    private void BuildWorkshopTargetRaft()
    {
        if(!IncludeScenery)return;
        var plank=_art.Toy(new("a18158"),.86f);var post=_art.Toy(new("63503b"),.9f);
        // The existing target is a core teaching object on Low. Boat, islet and
        // gulls remain optional; no further harbour props are added.
        var raft=Prop(this,"TargetRaft",new(Width+3.6f,-.53f,1.4f),-.3f);
        _workshopRaft=raft;
        RoundedBox(raft,new(0,.06f,0),new(1.8f,.16f,1.8f),plank,.16f);
        var board=Prop(raft,"TargetBoard",new(0,.95f,0));
        foreach(var(radius,color)in new[]{(.62f,"e5ddbf"),(.46f,"397d78"),(.3f,"e5ddbf"),(.14f,"c7ad6c")})
            Cylinder(board,new(0,0,.02f*(1-radius)),radius,.05f,_art.Color(new(color),.7f)).Basis=PresentationMath.Rotation(new(Mathf.Pi/2,0,0));
        Cylinder(raft,new(0,.42f,-.05f),.05f,.8f,post);
    }

    private void ProcessWorkshop(float delta)
    {
        if(_workshopRaft is null||!WeatherActive)return;
        _workshopClock+=delta;
        _workshopRaft.Position=new(_workshopRaft.Position.X,SceneryWaterHeight-.02f+PresentationMath.Sin(_workshopClock*1.3f+1.1f)*.05f,_workshopRaft.Position.Z);
        _workshopRaft.Basis=PresentationMath.Rotation(new(PresentationMath.Sin(_workshopClock*1.1f+1.1f)*.03f,
            -.3f+PresentationMath.Sin(_workshopClock*.25f+1.1f)*.08f,PresentationMath.Sin(_workshopClock*.9f+1.43f)*.05f));
    }

    private static void WorkshopQuad(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
    {WorkshopTriangle(surface,a,b,c,outward);WorkshopTriangle(surface,a,c,d,outward);}
    private static void WorkshopTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
    {
        var n=(c-a).Cross(b-a);if(n.LengthSquared()<1e-12f)return;n=n.Normalized();
        if(n.Dot(outward)<0){(b,c)=(c,b);n=-n;}
        foreach(var p in new[]{a,b,c}){surface.SetNormal(n);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
    }

    private const string WorkshopSurfaceShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 arena;
void vertex(){arena=VERTEX;}
float stripe(float distance,float width){return 1.0-smoothstep(width,width+.025,abs(distance));}
void fragment(){
 vec2 p=arena.xz;
 vec3 cream=vec3(.64,.62,.49),teal=vec3(.055,.25,.23);
 float brush=sin(p.x*.31+sin(p.y*.23))*.018;
 ALBEDO=cream+brush;
 // Accepted lesson start, move target and central staircase route.
 float range=step(5.0,p.x)*step(p.x,18.0);
 float rails=max(stripe(p.y-11.05,.065),stripe(p.y-14.95,.065))*range;
 float measured=step(8.25,p.x)*step(p.x,18.0);
 float ticks=stripe(mod(p.x-8.5+.5,1.0)-.5,.045)*stripe(p.y-12.5,.27)*measured;
 float axis=stripe(p.y-12.5,.035)*measured;
 float pad=stripe(length(p-vec2(8.5,12.5))-.48,.045);
 float stair_axis=stripe(p.x-11.5,.06)*step(1.0,p.y)*step(p.y,11.0);
 float stair_tick=stripe(mod(p.y-.5+1.0,2.0)-1.0,.045)*stripe(p.x-11.5,.27)*step(p.y,11.0);
 float target=0.0;
 if(arena.y>2.6||p.x>20.0){
   vec2 center=p.x>20.0?vec2(21.5,11.5):vec2(11.5,1.5);
   vec2 d=abs(p-center);
   target=max(stripe(d.x,.055)*step(d.y,.66),stripe(d.y,.055)*step(d.x,.77));
 }
 float inlay=max(max(max(rails,ticks),max(axis,pad)),max(stair_axis,max(stair_tick,target)));
 ALBEDO=mix(ALBEDO,teal,inlay*.90);
 // Pale tread marks sit on the actual continuous ramp planes.
 if(p.x>=10.0&&p.x<=13.0&&(p.y>=3.0&&p.y<4.0||p.y>=7.0&&p.y<8.0)){
   float tread=stripe(mod(p.y*6.0+.5,1.0)-.5,.075);
   ALBEDO=mix(ALBEDO,teal,tread*.55);
 }
 ROUGHNESS=.88;SPECULAR=.22;
}
""";

    private const string WorkshopLampGlowShader="""
shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled, depth_draw_never;
uniform float lamp_strength=0.0;
void vertex(){
 vec3 center=(VIEW_MATRIX*MODEL_MATRIX*vec4(0,0,0,1)).xyz;
 POSITION=PROJECTION_MATRIX*vec4(center+vec3(VERTEX.xy,0),1);
}
void fragment(){
 float soft=1.0-smoothstep(.06,.50,length(UV-.5));
 ALBEDO=vec3(1.0,.69,.30);ALPHA=soft*soft*.17*lamp_strength;
}
""";
}
