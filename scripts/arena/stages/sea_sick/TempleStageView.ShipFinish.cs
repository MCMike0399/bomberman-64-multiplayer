using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildShipFinish(Material oak,Material dark,Material brass)
    {
        var panel=ShipPaint("392d28",true);var frame=ShipPaint("92663f",true);
        AddChild(new MeshInstance3D{Name="CastleEndClosures",Mesh=ShipEndClosureMesh(),MaterialOverride=dark});
        foreach(var(from,to) in new[]{(0f,3f),(16f,19f)})foreach(bool near in new[]{true,false})
            for(int i=0;i<3;i++)
            {
                float x=Mathf.Lerp(from,to,(i+.5f)/3),baseY=TubbySection(x).Sheer+.13f,height=1.795f-.13f-baseY;
                var root=new Node3D{Name="CastleInset",Position=new(x,baseY+height/2,near?18.61f:.39f),
                    Basis=PresentationMath.Rotation(new(0,near?0:Mathf.Pi,0))};AddChild(root);
                ShipInset(root,.80f,height,frame,panel);
            }
        foreach(float v in new[]{-.62f,0f,.62f})
        {
            float x=-1.05f-.35f*(1-v*v)+.23f,z=9.5f+8.25f*v;
            var root=new Node3D{Name="TaffrailInset",Position=new(x,2.11f,z),Basis=PresentationMath.Rotation(new(0,Mathf.Pi/2,0))};AddChild(root);
            ShipInset(root,2.4f,1.35f,frame,panel);
        }
        BuildSternLantern(dark,brass);
        // The cosmetic masthead exits the battle frame. Its visible signal
        // collar carries one pennant below the HUD, replacing the stern flag.
        var pennant=new Node3D{Name="MastSignalPennant",Position=new(9.87f,5.4f,8.5f)};AddChild(pennant);
        var cloth=new ShaderMaterial{Shader=new Shader{Code=ShipSignalPennantShader}};
        var flag=Plate(pennant,Vector3.Zero,[new(0,0),new(1.5f,-.08f),new(1.18f,-.52f),new(0,-.43f)],.025f,cloth);
        flag.Name="CutterSignalCloth";flag.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        _shipLife.Add(t=>
        {
            cloth.SetShaderParameter("life_clock",t);
            pennant.Basis=PresentationMath.Rotation(new(0,.16f+PresentationMath.Sin(t*.8f)*.12f,-GlobalBasis.Z.Y*.6f));
        });
    }

    private const string ShipSignalPennantShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform float life_clock=0.0;
void vertex(){
    float weight=max(VERTEX.x,0.0)/1.5;
    VERTEX.z+=sin(life_clock*2.3-VERTEX.x*3.0)*.075*weight;
    VERTEX.y+=sin(life_clock*1.7-VERTEX.x*2.0)*.025*weight;
}
void fragment(){
    float v=clamp(-UV.y/.52,0.0,1.0);
    float hem=smoothstep(.04,.10,min(v,1.0-v));
    ALBEDO=mix(vec3(.73,.53,.25),vec3(.52,.14,.10),hem);ROUGHNESS=.9;
}
""";

    private static void ShipInset(Node root,float width,float height,Material frame,Material panel)
    {
        const float rim=.12f;
        Box(root,new(0,0,-.025f),new(width-rim*2,height-rim*2,.04f),panel,.025f);
        foreach(float sign in new[]{-1f,1f})
        {
            Box(root,new(0,sign*(height-rim)/2,.025f),new(width,rim,.13f),frame,.025f);
            Box(root,new(sign*(width-rim)/2,0,.025f),new(rim,height-rim*2,.13f),frame,.025f);
        }
    }

    private void BuildSternLantern(Material dark,Material brass)
    {
        ShipBeam(new(-1.12f,3.12f,9.5f),new(-.72f,3.64f,9.5f),.13f,.13f,brass).Name="SternLanternBracket";
        var hinge=new Node3D{Name="SternLantern",Position=new(-.72f,3.59f,9.5f)};AddChild(hinge);
        _shipLanternGlow=(StandardMaterial3D)_art.Toy(new("d7a96c"),.55f,true).Duplicate();
        _shipLanternGlow.Emission=new("ffc67b");
        Box(hinge,new(0,-.47f,0),new(.48f,.63f,.48f),_shipLanternGlow,.065f);
        foreach(float x in new[]{-.28f,.28f})foreach(float z in new[]{-.28f,.28f})
            Box(hinge,new(x,-.47f,z),new(.10f,.75f,.10f),brass,.025f);
        Box(hinge,new(0,-.87f,0),new(.68f,.13f,.68f),brass,.04f);
        ShipLathe(hinge,new(0,-.09f,0),[(0,.37f),(.16f,.24f),(.28f,.08f)],dark,"lantern-roof");
        _shipLanternLight=new OmniLight3D{Name="SternLanternGlow",Position=new(0,-.46f,.10f),LightColor=new("ffc682"),
            OmniRange=6.5f,LightEnergy=.1f,ShadowEnabled=false};hinge.AddChild(_shipLanternLight);
        _shipLife.Add(t=>hinge.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(t*.9f)*.09f)));
    }

    private void ApplyShipLighting(StageLighting lighting)
    {
        bool night=lighting.Label=="Night";
        if(_shipLanternGlow!=null)_shipLanternGlow.EmissionEnergyMultiplier=night?1.10f:.06f;
        if(_shipLanternLight!=null)_shipLanternLight.LightEnergy=night?.85f:.08f;
        if(_water?.MaterialOverride is not ShaderMaterial water)return;
        water.SetShaderParameter("deep_color",lighting.DeepWater.Lerp(new Color("102a3c"),.45f));
        water.SetShaderParameter("surface_color",lighting.ShallowWater.Lerp(new Color("2d6378"),.55f));
        water.SetShaderParameter("foam_color",lighting.Foam.Lerp(new Color("9fc6cc"),.35f));
        water.SetShaderParameter("wind_strength",lighting.Wind*1.65f);
        water.SetShaderParameter("surface_roughness",lighting.WaterRoughness+.035f);
    }

    private static ArrayMesh ShipEndClosureMesh()
    {
        const string key="ship-end-closures";if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal)
        {
            if((c-a).Cross(b-a).Dot(normal)<0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.Z,p.Y));surface.AddVertex(p);}
        }
        foreach(float end in new[]{0f,19f})for(int i=0;i<40;i++)
        {
            float za=.4f+18.2f*i/40,zb=.4f+18.2f*(i+1)/40;
            float Top(float z)=>Math.Max(1.35f,TubbyCapPoint(end,Mathf.Clamp((z-9.5f)/TubbySection(end).Beam,-1,1)).Y);
            float x=end==0?-.015f:19.015f,bottom=TubbySection(end).Sheer-.03f;
            Vector3 a=new(x,bottom,za),b=new(x,bottom,zb),c=new(x,Top(zb),zb),d=new(x,Top(za),za);
            foreach(var normal in new[]{Vector3.Left,Vector3.Right}){Triangle(a,b,c,normal);Triangle(a,c,d,normal);}
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }
}
