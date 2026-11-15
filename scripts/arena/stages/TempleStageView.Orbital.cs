using BlastCircuit.Rendering;
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
        scenery.AddChild(new MeshInstance3D{Name="Nebula",Mesh=PrimitiveTemplates.Get("BoxMesh",("size",new Vector3(190,.05f,190))),
            MaterialOverride=nebula,Position=new(Width/2f,-49,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});

        var giant=Prop(scenery,"RingedPlanet",new(-9.6f,-8,1.5f));
        var gas=_art.Planet(new("625b7b"),new("cbb79f"));_ambientMaterials.Add(gas);
        var globe=Sphere(giant,Vector3.Zero,Vector3.One*6.2f,gas);
        globe.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;globe.Basis=PresentationMath.Rotation(new(-.25f,0,.32f),Vector3.One*6.2f);
        var rings=Prop(giant,"PlanetRings",Vector3.Zero);rings.Basis=PresentationMath.Rotation(new(-.25f,0,.32f));
        var ringPaint=_art.PlanetRings();
        // Broad painted discs with a real division read as a tiny astronomical
        // model. Opaque faces avoid luminous wire rings and transparent sorting.
        foreach(var (inner,outer) in new[]{(3.65f,4.22f),(4.36f,4.9f)})
        {
            var ring=new MeshInstance3D{Name="PaintedRing",Mesh=OrbitalAnnulus(inner,outer),MaterialOverride=ringPaint,
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            rings.AddChild(ring);
        }
        _sceneryMotion.Add(t=>giant.Position=new(-9.6f+Mathf.Sin(t*.025f)*.45f,-8+Mathf.Sin(t*.04f)*.22f,1.5f));

        var blue=Prop(scenery,"BluePlanet",new(Width+10.8f,-12,6.7f));
        var ocean=_art.Planet(new("293d68"),new("6b9fb3"));_ambientMaterials.Add(ocean);
        var blueGlobe=Sphere(blue,Vector3.Zero,Vector3.One*7.1f,ocean);
        blueGlobe.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;blueGlobe.Basis=PresentationMath.Rotation(new(-.3f,0,-.38f),Vector3.One*7.1f);
        var moon=Prop(scenery,"OrbitingMoon",new(Width+7,-7,-2));
        var moonPaint=_art.Planet(new("68647e"),new("b9bbca"),true);_ambientMaterials.Add(moonPaint);
        Sphere(moon,Vector3.Zero,Vector3.One*1.95f,moonPaint).CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
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

    /// <summary>Thin double-sided annulus. The centre and Cassini-like division
    /// are actual empty space, so the globe naturally occludes the rear half.</summary>
    private static ArrayMesh OrbitalAnnulus(float inner,float outer)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=96;
        void Vertex(float angle,float radius)
        {
            var (sin,cos)=PresentationMath.SinCos(angle);
            var p=new Vector3(cos*radius,0,sin*radius);
            surface.SetNormal(Vector3.Up);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);
        }
        for(int i=0;i<segments;i++)
        {
            float a=i*Mathf.Tau/segments,b=(i+1)*Mathf.Tau/segments;
            Vertex(a,inner);Vertex(b,outer);Vertex(b,inner);
            Vertex(a,inner);Vertex(a,outer);Vertex(b,outer);
        }
        surface.Index();return surface.Commit();
    }
}
