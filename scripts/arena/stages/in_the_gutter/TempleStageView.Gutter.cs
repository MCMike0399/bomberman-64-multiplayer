using Godot;
using System.Text.Json;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    public ShaderMaterial? GutterWakeMaterial=>_gutterWakeMaterial;

    public void SetGutter(JsonElement current)
    {
        if(!_gutter||current.ValueKind!=JsonValueKind.Object)return;
        _gutterClock=current.GetProperty("time").GetSingle();
        float next=current.GetProperty("x").GetSingle();
        int direction=Math.Sign(next);
        if(direction!=0&&direction!=_gutterDirection)
        {
            _gutterDirection=direction;
        }
        _gutterFlow=next;
    }

    private void ProcessGutter(float delta)
    {
        if(WeatherActive)
        {
            _gutterClock+=delta;
            _gutterFishYaw=Mathf.LerpAngle(_gutterFishYaw,_gutterDirection<0?Mathf.Pi:0,1-Mathf.Exp(-3.5f*delta));
            _gutterGearPose+=_gutterFlow/GutterRules.CurrentSpeed*.22f*delta;
            if(_gutterDialNeedle!=null)
            {
                _gutterDialAngle=Mathf.Lerp(_gutterDialAngle,_gutterFlow/GutterRules.CurrentSpeed*.75f,1-Mathf.Exp(-4*delta));
                _gutterDialNeedle.Basis=PresentationMath.Rotation(new(0,0,_gutterDialAngle));
            }
            foreach(var (gear,phase) in _gutterGears)
                gear.Basis=PresentationMath.Rotation(new(0,0,_gutterGearPose+phase));
            foreach(var (leaf,phase) in _gutterLeaves)
            {
                float x=leaf.Position.X+_gutterFlow*delta*.65f;
                if(x<1.4f)x=Width-1.4f;if(x>Width-1.4f)x=1.4f;
                leaf.Position=new(x,.078f,leaf.Position.Z);
                leaf.Basis=PresentationMath.Rotation(new(-Mathf.Pi/2,_gutterClock*.04f+phase,0));
            }
        }
        if(_gutterArrow!=null)_gutterArrow.Basis=PresentationMath.Rotation(new(0,_gutterFishYaw,0));
        if(_gutterWater!=null&&_graphicsRevision!=GraphicsQuality.Revision)
        {
            _gutterWater.Shader=ArtMaterials.GutterWaterShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(_gutterWater);
            if(_lighting!=null)_lighting.ApplyWater(_gutterWater);
            _gutterWater.SetShaderParameter("wind_strength",.2f);
            if(_gutterOceanWater!=null)
            {
                _gutterOceanWater.Shader=ArtMaterials.GutterOceanShaderFor(GraphicsQuality.Current);
                GraphicsQuality.ApplyShaderDetail(_gutterOceanWater);ApplyGutterOceanLighting();
            }
            foreach(var material in _gutterMaterials)GraphicsQuality.ApplyShaderDetail(material);
            foreach(var light in _gutterWorkLights)light.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
            _graphicsRevision=GraphicsQuality.Revision;
        }
        if(_gutterWakeMaterial!=null)
        {
            _gutterWakeMaterial.SetShaderParameter("current_clock",_gutterClock);
            _gutterWakeMaterial.SetShaderParameter("current",_gutterFlow);
        }
    }

    private void BuildGutter()
    {
        Name="CopperLockTerrain";
        var brick=_art.Toy(new Color("753d48"),.66f);
        var brass=_art.Toy(new Color("c7a266"),.6f);
        var cream=_art.Toy(new Color("e5d7b6"),.79f);
        var verdigris=_art.Toy(new Color("39756f"),.72f);
        var algae=_art.Color(new Color("466b57"),.9f);
        var dark=_art.Color(new Color("253c3b"),.8f);
        _gutterWakeMaterial=new ShaderMaterial{Shader=new Shader{Code=GutterWakeShader},RenderPriority=-15};
        _gutterMaterials.Add(_gutterWakeMaterial);
        var mosaic=new ShaderMaterial{Shader=new Shader{Code=GutterMosaicShader}};
        _gutterMaterials.Add(mosaic);
        // A sculpted, stepped foot supports the lock rather than a uniform tray lip.
        RoundedBox(this,new(Width/2f,-.72f,Depth/2f),new(Width+.95f,1.1f,Depth+.85f),brick,.18f).Name="CopperLockFoundation";
        Box(this,new(Width/2f,-.17f,Depth/2f),new(Width-2,.2f,Depth-2),mosaic,.035f).Name="GutterSubmergedPaving";
        _gutterWater=new ShaderMaterial{Shader=ArtMaterials.GutterWaterShaderFor(GraphicsQuality.Current),RenderPriority=-16};
        _gutterWater.SetShaderParameter("wind_strength",.2f);_gutterWater.SetShaderParameter("seabed_height",-1f);
        GutterWaterPlane(this,"GutterShallowWater",1,Width-1,1,Depth-1,.035f,_gutterWater);

        // These perimeter cells are not walkable: a half-round coping
        // communicates a barrier while staying within the authored crown.
        foreach(int z in new[]{0,Depth-1})
        {
            float h=(Layout.At(1,z).ObstacleHeight??.35f)*HeightScale;
            for(int x=1;x<Width-1;x++)
                RoundedBox(this,new(x+.5f,(h-.25f)/2,z+.5f),new(.998f,h-.25f,.998f),brick,.12f);
            GutterHalfRoundCoping(z+.5f,h,cream);
        }
        GutterGantry(this,-1,verdigris,brass,cream);
        GutterGantry(this,1,verdigris,brass,cream);
        GutterWorkLantern(new(.24f,2.35f,Depth+.42f),verdigris,brass);
        GutterWorkLantern(new(Width-.24f,2.35f,Depth+.42f),verdigris,brass);
        foreach(int x in new[]{4,15})foreach(int z in new[]{4,15})GutterBollard(x,z,brick,cream,brass,algae);
        GutterFishVane(brick,cream,brass,dark);
        BuildCopperLockEnds(brick,cream,verdigris,brass,dark,mosaic);
        BuildGutterLeaves();
        if(IncludeScenery)BuildCopperLockCoreWater();
    }

    private static MeshInstance3D GutterWaterPlane(Node3D parent,string name,float left,float right,float back,float front,float height,Material material,int divisions=20)
    {
        // Shared water centers its radial effects at NODE_POSITION_WORLD.
        // Localizing this grid preserves its world footprint and UVs.
        float x=(left+right)/2,z=(back+front)/2;
        var node=new MeshInstance3D{Name=name,Mesh=GutterPlane(left-x,right-x,back-z,front-z,divisions),
            Position=new(x,height,z),MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        parent.AddChild(node);return node;
    }

    private static ArrayMesh GutterPlane(float left,float right,float back,float front,int divisions=20)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Vertex(float u,float v)
        {
            surface.SetNormal(Vector3.Up);surface.SetUV(new(u,v));
            surface.AddVertex(new(Mathf.Lerp(left,right,u),0,Mathf.Lerp(back,front,v)));
        }
        for(int z=0;z<divisions;z++)for(int x=0;x<divisions;x++)
        {
            float a=x/(float)divisions,b=(x+1f)/divisions,c=z/(float)divisions,d=(z+1f)/divisions;
            Vertex(a,c);Vertex(b,c);Vertex(b,d);Vertex(a,c);Vertex(b,d);Vertex(a,d);
        }
        surface.Index();return surface.Commit();
    }

    private Node3D CreateGutterCover(int x,int z)
    {
        var root=new Node3D{Name="GutterMasonryCover",Position=new(x+.5f,Surface(x+.5f,z+.5f),z+.5f)};
        var light=_art.Toy(new Color("b9c8b8"),.81f);var seam=_art.Color(new Color("69877a"),.9f);
        float height=Layout.SoftBlockHeight*HeightScale;
        Box(root,new(0,height/2,0),new(.82f,height,.82f),light,.085f);
        Box(root,new(0,height*.48f,.405f),new(.69f,.07f,.025f),seam,.015f);
        AddGutterWake(root,new(0,.072f,0),.84f);
        AddGutterFoam(root,new(0,.074f,0),.9f);
        return root;
    }

    private const string GutterMosaicShader="""
shader_type spatial;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    vec2 p=world.xz/2.8;
    vec2 f=fract(p);
    float edge=min(min(f.x,1.0-f.x),min(f.y,1.0-f.y));
    float aa=max(fwidth(edge),.002);
    float tile=smoothstep(.012-aa,.032+aa,edge);
    float alternate=mod(floor(p.x)+floor(p.y),2.0);
    vec3 paint=mix(vec3(.36,.51,.44),vec3(.38,.53,.46),alternate);
    ALBEDO=mix(vec3(.31,.47,.40),paint,tile);
    ROUGHNESS=.82;SPECULAR=.2;
}
""";
}
