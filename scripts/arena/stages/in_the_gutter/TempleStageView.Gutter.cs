using Godot;
using System.Text.Json;
using Bomber.Game.Simulation.Rules;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    public void SetGutter(JsonElement current)
    {
        if(!_gutter||current.ValueKind!=JsonValueKind.Object)return;
        _gutterClock=current.GetProperty("time").GetSingle();
        _gutterFlow=current.GetProperty("x").GetSingle();
    }

    private void ProcessGutter(float delta)
    {
        if(WeatherActive)_gutterClock+=delta;
        if(_gutterArrow!=null)
        {
            _gutterArrow.Basis=PresentationMath.Rotation(new(0,_gutterFlow<0?Mathf.Pi:0,0),Vector3.One*(Math.Abs(_gutterFlow)<.05f?.65f:1));
        }
        if(_gutterWater!=null&&_graphicsRevision!=GraphicsQuality.Revision)
        {
            _gutterWater.Shader=ArtMaterials.GutterWaterShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(_gutterWater);
            if(_lighting!=null)_lighting.ApplyWater(_gutterWater);
            foreach(var material in _gutterMaterials)GraphicsQuality.ApplyShaderDetail(material);
            _graphicsRevision=GraphicsQuality.Revision;
        }
        // Accepted flow drives the visible water tracer; pause freezes both.
        if(_gutterTracer!=null)
        {
            _gutterTracer.SetShaderParameter("current_clock",_gutterClock);
            _gutterTracer.SetShaderParameter("current",_gutterFlow);
        }
    }

    private void BuildGutter()
    {
        Name="GutterWaterworksTerrain";
        var brick=_art.Masonry();brick.SetShaderParameter("base_color",new Color("814a43"));
        brick.SetShaderParameter("mortar_color",new Color("473d3b"));brick.SetShaderParameter("water_height",.035f);
        var stone=_art.Stone(true);stone.SetShaderParameter("base_color",new Color("8cae9e"));
        stone.SetShaderParameter("mortar_color",new Color("3d665e"));stone.SetShaderParameter("water_height",.035f);
        _gutterMaterials.AddRange([brick,stone]);
        var brass=_art.Toy(new Color("c8a15e"),.58f);
        var cap=_art.Toy(new Color("e8d5a3"),.72f);
        var iron=_art.Toy(new Color("3e6263"),.66f);
        Box(this,new(Width/2f,-.65f,Depth/2f),new(Width+.7f,1.05f,Depth+.7f),brick,.1f).Name="GutterLockFoundation";
        Box(this,new(Width/2f,-.15f,Depth/2f),new(Width-2,.16f,Depth-2),stone,.04f).Name="GutterSubmergedPaving";
        // An owned grid avoids requesting an uncaptured engine-primitive domain.
        _gutterWater=new ShaderMaterial{Shader=ArtMaterials.GutterWaterShaderFor(GraphicsQuality.Current),RenderPriority=-16};_gutterWater.SetShaderParameter("wind_strength",.2f);
        _gutterWater.SetShaderParameter("seabed_height",-1f);
        var water=new MeshInstance3D{Name="GutterShallowWater",Mesh=GutterPlane(1,Width-1,1,Depth-1),
            MaterialOverride=_gutterWater,Position=new(0,.035f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        AddChild(water);
        var tracer=_gutterTracer=new ShaderMaterial{Shader=new Shader{Code=GutterFlowShader},RenderPriority=-15};
        _gutterMaterials.Add(tracer);
        AddChild(new MeshInstance3D{Name="GutterFlowTracers",Mesh=GutterPlane(1.02f,Width-1.02f,1.02f,Depth-1.02f),
            MaterialOverride=tracer,Position=new(0,.075f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);
            if(x==0||x==Width-1)
            {
                float h=(cell.ObstacleHeight??2.1f)*HeightScale;
                Box(this,new(x+.5f,.14f,z+.5f),new(.96f,.28f,.99f),brick,.035f);
                for(int bar=0;bar<2;bar++)
                    Box(this,new(x+.5f,(h-.14f)/2,z+.25f+bar*.5f),new(.12f,h-.14f,.12f),iron,.025f).Name="GutterFixedGrate";
                Box(this,new(x+.5f,h-.07f,z+.5f),new(.2f,.14f,.98f),brass,.035f);
            }
            else if(z==0||z==Depth-1)
            {
                float h=(cell.ObstacleHeight??.35f)*HeightScale;
                Box(this,new(x+.5f,(h-.09f)/2,z+.5f),new(.99f,h-.09f,.99f),brick,.04f);
                Box(this,new(x+.5f,h-.045f,z+.5f),new(1.02f,.09f,1.02f),cap,.025f);
            }
            else if(cell.Tier>0)
            {
                float y=cell.Tier*HeightScale;
                Box(this,new(x+.5f,(y-.1f)/2,z+.5f),new(.998f,y-.1f,.998f),brick,.03f).Name="GutterRaisedPost";
                // The cap owns the top plane, at exactly the authority elevation.
                Box(this,new(x+.5f,y-.05f,z+.5f),new(.998f,.1f,.998f),cap,.025f);
            }
            else if(cell.Kind==Bomber.Game.Simulation.Stages.CellKind.Pillar)
            {
                float h=(cell.ObstacleHeight??1.1f)*HeightScale;
                Box(this,new(x+.5f,(h-.12f)/2,z+.5f),new(.998f,h-.12f,.998f),brick,.03f).Name="GutterCenterPost";
                Box(this,new(x+.5f,h-.06f,z+.5f),new(1.02f,.12f,1.02f),brass,.035f);
                _gutterArrow=new Node3D{Name="GutterCurrentMarker",Position=new(x+.5f,h+.17f,z+.5f)};
                AddChild(_gutterArrow);
                Plate(_gutterArrow,Vector3.Zero,[new(-.43f,-.09f),new(.08f,-.09f),new(.08f,-.24f),new(.47f,0),new(.08f,.24f),new(.08f,.09f),new(-.43f,.09f)],.12f,_art.Toy(new Color("f3c857"),.55f));
            }
        }
    }

    private static ArrayMesh GutterPlane(float left,float right,float back,float front)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int divisions=20;
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
        var light=_art.Toy(new Color("c4cec1"),.84f);var dark=_art.Color(new Color("617a73"),.9f);
        float height=Layout.SoftBlockHeight*HeightScale;
        Box(root,new(0,height/2,0),new(.82f,height,.82f),light,.035f);
        for(int course=1;course<3;course++)
            Box(root,new(0,height*course/3,.413f),new(.8f,.025f,.012f),dark);
        for(int course=0;course<3;course++)
            Box(root,new(course%2==0?-.15f:.15f,height*(course+.5f)/3,.414f),new(.025f,height/3-.025f,.014f),dark);
        return root;
    }

    public Node3D CreateGutterClosure(int column)
    {
        var root=new Node3D{Name="ClosingGutterGrate"+column};
        var metal=_art.Toy(new Color("42605b"),.64f);var brass=_art.Toy(new Color("d2a45c"),.62f);
        float x=column<Width/2f?column+.93f:column+.07f,height=2.1f*HeightScale;
        // A settled volume matches the newly lethal column, while its narrow
        // leading grille keeps the surviving court readable through the bars.
        Box(root,new(column+.5f,.1f,Depth/2f),new(1,.2f,Depth-2),metal,.035f);
        for(float z=1.25f;z<Depth-1;z+=.5f)
            Box(root,new(x,height/2,z),new(.14f,height,.12f),metal,.025f);
        Box(root,new(x,height-.08f,Depth/2f),new(.22f,.16f,Depth-2),brass,.035f);
        return root;
    }

    private const string GutterFlowShader="""
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
uniform float current_clock=0.0;
uniform float current=0.0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    float lane=floor(world.z*.8);
    float x=fract((world.x-current_clock*current)*.21+lane*.37);
    float z=fract(world.z*.8);
    float trace=smoothstep(.0,.1,x)*(1.0-smoothstep(.16,.62,x));
    trace*=smoothstep(.44,.48,z)*(1.0-smoothstep(.5,.54,z));
    ALBEDO=vec3(.7,.95,.88);
    ALPHA=trace*.23*min(1.0,abs(current));
}
""";
}
