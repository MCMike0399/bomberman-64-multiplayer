using Godot;
using System.Text.Json;
using Bomber.Game.Simulation;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Effects;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena;

public partial class TempleStageView
{
    public bool WeatherActive {get;set;}=true;
    public BlizzardWeather? Weather=>_blizzardWeather;
    public void SetBlizzard(JsonElement weather)
    {
        if(!_blizzard||weather.ValueKind!=JsonValueKind.Object)return;
        _blizzardClock=weather.GetProperty("time").GetSingle();
        _blizzardWind=new(weather.GetProperty("x").GetSingle(),weather.GetProperty("z").GetSingle());
        if(_blizzardWeather!=null)_blizzardWeather.Wind=_blizzardWind;
    }
    private void ProcessBlizzard(float delta)
    {
        if(WeatherActive)_blizzardClock+=delta;
        foreach(var material in _frozenMaterials)
        {
            material.SetShaderParameter("weather_clock",_blizzardClock);
            material.SetShaderParameter("wind",_blizzardWind);
            if(_graphicsRevision!=GraphicsQuality.Revision)
                material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
        }
        _graphicsRevision=GraphicsQuality.Revision;
        if(_blizzardWeather!=null)_blizzardWeather.Active=WeatherActive;
        for(int i=_avalancheSwells.Count-1;i>=0;i--)
        {
            var (bank,age)=_avalancheSwells[i];
            if(!GodotObject.IsInstanceValid(bank)||bank.IsQueuedForDeletion()){_avalancheSwells.RemoveAt(i);continue;}
            if(!WeatherActive)continue;
            age+=delta;
            float blend=1-Mathf.Pow(1-Mathf.Clamp(age/.32f,0,1),3);
            float direction=bank.GetChild<Node3D>(0).Position.X<Width/2f?-1:1;
            bank.Position=new(direction*(1-blend)*.7f,0,0);
            if(age>=.32f)_avalancheSwells.RemoveAt(i);else _avalancheSwells[i]=(bank,age);
        }
    }
    private void BuildBlizzard()
    {
        Name="BlizzardBattleTerrain";
        var ice=_art.FrozenSurface(0);_crystalMaterial=_art.FrozenSurface(1);
        _snowMaterial=_art.FrozenSurface(2);var wall=_art.FrozenSurface(3);
        _frozenMaterials.AddRange([ice,_crystalMaterial,_snowMaterial,wall]);
        Box(this,new(Width/2f,-.55f,Depth/2f),new(160,.45f,160),_snowMaterial).Name="FrozenExterior";
        Box(this,new(Width/2f,-.4f,Depth/2f),new(Width,.6f,Depth),wall,.08f).Name="GlacierFoundation";
        Box(this,new(Width/2f,-.055f,Depth/2f),new(Width-2,.11f,Depth-2),ice,.035f).Name="FrozenCourt";
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);if(cell.Kind!=CellKind.Pillar)continue;
            float h=(cell.ObstacleHeight??BombThrowRules.PillarHeight)*HeightScale;
            Box(this,new(x+.5f,h*.5f-.125f,z+.5f),new(1,h+.05f,1),wall,.06f).Name="GlacierWall";
            Box(this,new(x+.5f,h-.075f,z+.5f),new(1.04f,.15f,1.04f),_snowMaterial,.065f).Name="SnowWallCap";
            // The inner blue band makes the solid limit easy to read through snow.
            if(x==0||x==Width-1)
                Box(this,new(x==0?.96f:Width-.96f,.075f,z+.5f),new(.075f,.15f,.98f),ice,.02f);
            if(z==0||z==Depth-1)
                Box(this,new(x+.5f,.075f,z==0?.96f:Depth-.96f),new(.98f,.15f,.075f),ice,.02f);
        }
        _iceCones=Enumerable.Range(0,4).Select(IceConeMesh).ToArray();
        _blizzardWeather=new BlizzardWeather{Width=Width,Depth=Depth};AddChild(_blizzardWeather);
    }
    private Node3D CreateIceCone(Node3D root,int cell)
    {
        root.Name="IceCone"+cell;
        root.AddChild(new MeshInstance3D{Mesh=_iceCones[cell%_iceCones.Length],MaterialOverride=_crystalMaterial});
        root.Rotation=new(0,cell*2.39996f,0);
        return root;
    }
    private static ArrayMesh IceConeMesh(int variant)
    {
        const int sides=5;
        float h=BombThrowRules.CoverHeight*HeightScale;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Ring(int i,float y,float radius){float a=i*Mathf.Tau/sides;return new(Mathf.Cos(a)*radius,y,Mathf.Sin(a)*radius);}
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 normal=(c-a).Cross(b-a).Normalized();
            if(normal.Dot((a+b+c)/3-new Vector3(0,h*.25f,0))<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
        }
        var tip=new Vector3(.08f*Mathf.Sin(variant+1),h,.08f*Mathf.Cos(variant+1));
        for(int i=0;i<sides;i++)
        {
            var a=Ring(i,.015f,.43f);var b=Ring(i+1,.015f,.43f);
            var c=Ring(i+1,h*.11f,.22f);var d=Ring(i,h*.11f,.22f);
            Triangle(a,b,c);Triangle(a,c,d);Triangle(d,c,tip);
        }
        return surface.Commit();
    }
    public MeshInstance3D CreateIceShard(Node parent,Vector3 position,int variant)
    {
        var shard=new MeshInstance3D{Name="IceShard",Position=position,Mesh=_iceCones[variant%_iceCones.Length],
            MaterialOverride=_crystalMaterial,Scale=Vector3.One*(.21f+variant*.015f)};
        parent.AddChild(shard);return shard;
    }
    public Node3D CreateAvalanche(int column)
    {
        var root=new Node3D{Name="AvalancheColumn"+column};
        float x=column+.5f;
        Box(root,new(x,.6f,Depth/2f),new(1,1.5f,Depth-2),_snowMaterial,.12f);
        for(int z=1;z<Depth-1;z++)
        {
            float h=1.3f+.12f*Mathf.Sin(z*2.3f+column);
            Sphere(root,new(x,h,z+.5f),new(.99f,.65f,1.1f),_snowMaterial);
        }
        // A short advancing swell, then settled packed snow. Collision is snapshot-owned.
        root.Position=new(column<Width/2?-.7f:.7f,0,0);
        _avalancheSwells.Add((root,0));
        return root;
    }
}
