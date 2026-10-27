using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>An original planetary setting around the existing sky castle.
    /// Every orbit stays below the foundation and outside the court's silhouette.</summary>
    private void BuildOrbitalBackdrop()
    {
        var scenery=_scenery!;
        var nebula=_art.Nebula();_ambientMaterials.Add(nebula);
        nebula.SetShaderParameter("stage_center",new Vector2(Width/2f,Depth/2f));
        scenery.AddChild(new MeshInstance3D{Name="Nebula",Mesh=new BoxMesh{Size=new(190,.05f,190)},
            MaterialOverride=nebula,Position=new(Width/2f,-49,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});

        var giant=Prop(scenery,"RingedPlanet",new(-10,-8,1.5f),0,1);
        var gas=_art.Planet(new("735d88"),new("e6bda1"));_ambientMaterials.Add(gas);
        var globe=Sphere(giant,Vector3.Zero,Vector3.One*6.4f,gas);
        globe.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;globe.Rotation=new(-.6f,0,.25f);
        var rings=Prop(giant,"PlanetRings",Vector3.Zero);rings.Rotation=new(-.6f,0,.25f);
        // Solid thin annuli keep the toy-like ring silhouette stable without alpha overdraw.
        for(int i=0;i<3;i++)
        {
            var ring=new MeshInstance3D{Mesh=new TorusMesh{InnerRadius=3.9f+i*.43f,OuterRadius=4.15f+i*.43f,Rings=64,RingSegments=8},
                Scale=new(1,.08f,1),MaterialOverride=Glow(new(i==1?"8979a4":"c6b6b1"),.6f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            rings.AddChild(ring);
        }
        _sceneryMotion.Add(t=>giant.Position=new(-10+Mathf.Sin(t*.025f)*.65f,-8+Mathf.Sin(t*.04f)*.3f,1.5f));

        var blue=Prop(scenery,"BluePlanet",new(Width+11,-12,8));
        var ocean=_art.Planet(new("254c86"),new("76b8cc"));_ambientMaterials.Add(ocean);
        var blueGlobe=Sphere(blue,Vector3.Zero,Vector3.One*8.5f,ocean);
        blueGlobe.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;blueGlobe.Rotation=new(-.5f,0,-.3f);
        var moon=Prop(scenery,"OrbitingMoon",new(Width+7,-7,-2));
        var moonPaint=_art.Planet(new("666a88"),new("c7cce2"),true);_ambientMaterials.Add(moonPaint);
        Sphere(moon,Vector3.Zero,Vector3.One*1.7f,moonPaint).CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        _sceneryMotion.Add(t=>
        {
            float a=t*.045f+.8f;
            moon.Position=new(Width+7+Mathf.Cos(a)*1.6f,-7+Mathf.Sin(a)*.4f,-2+Mathf.Sin(a)*1.6f);
        });

        // A slow debris stream, distinct from Rock Garden's dangerous fireballs:
        // no flames, trails, warning circles or passage over the arena.
        var belt=Prop(scenery,"AsteroidBelt",Vector3.Zero);
        var stone=_art.Color(new("777087"),.95f);
        for(int i=0;i<15;i++)
        {
            float phase=i*2.39996f,scale=.3f+(i%4)*.17f;
            bool right=i%2==0;
            var asteroid=Prop(belt,"DriftingAsteroid",Vector3.Zero,phase,scale);
            Rock(asteroid,new(0,-.4f,0),new(1.1f,.75f,.9f),stone,i);
            _sceneryMotion.Add(t=>
            {
                float u=t*.065f+phase;
                asteroid.Position=new(right?Width+4.7f+Mathf.Sin(u)*1.2f:-5.5f+Mathf.Sin(u)*1.1f,
                    -4.8f+Mathf.Sin(u*.7f)*.6f,Mathf.PosMod(t*.2f+i*2.3f,Depth+14)-7);
                asteroid.Rotation=new(phase+t*.065f,t*.085f+phase,Mathf.Sin(u)*.3f);
            });
        }
    }
}
