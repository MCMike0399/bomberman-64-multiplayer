using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>A sky citadel in the castle's dark backdrop, informed by Bomberman 64's
    /// Black Fortress descending through the clouds: a rocky crag below the foundation,
    /// four corner turrets with braziers and pennants, heraldic wall banners, drifting
    /// cloud banks, small floating islets and a starfield. None of it is walkable.</summary>
    private void BuildTopRulesExterior()
    {
        var scenery=_scenery!;
        var stone=_castleWallMaterial;
        var cap=_art.Color(new("bdc4c5"),.8f);var slate=_art.Toy(new("3d4f7a"),.5f);
        var gold=_art.Color(new("c79a3d"),.4f);var crimson=_art.Color(new("8c2233"),.8f);
        var iron=_art.Color(new("2c2f36"),.5f);

        // The crag: an irregular inverted spire that grows out of the foundation.
        // A plain painted rock keeps the castle's bounded set of quality-aware stone shaders.
        var rock=_art.Color(new("5b5860"),.95f);
        scenery.AddChild(new MeshInstance3D{Name="FortressCrag",Mesh=CragMesh(Width,Depth),MaterialOverride=rock});

        // Corner turrets: tall braziers at the rear, short roofed towers at the front
        // so the near wall stays low and the lower court remains visible.
        foreach(var (x,z,rear) in new[]{(-.45f,-.45f,true),(Width+.45f,-.45f,true),(-.45f,Depth+.45f,false),(Width+.45f,Depth+.45f,false)})
        {
            float height=rear?6.9f:3.15f,radius=rear?.92f:.7f;
            var turret=Prop(scenery,"Turret",new(x,-.94f,z));
            Cylinder(turret,new(0,height/2,0),radius,height,stone);
            Cylinder(turret,new(0,height-.05f,0),radius+.12f,.14f,cap);
            for(int band=1;band<=(rear?3:1);band++)Cylinder(turret,new(0,band*1.7f,0),radius+.03f,.08f,gold);
            if(rear)
            {
                for(int merlon=0;merlon<8;merlon++)
                {
                    float a=merlon*Mathf.Tau/8;
                    Box(turret,new(Mathf.Cos(a)*.86f,height+.2f,Mathf.Sin(a)*.86f),new(.34f,.36f,.26f),cap,.02f).Rotation=new(0,Mathf.Pi/2-a,0);
                }
                Taper(turret,new(0,height+.2f,0),.32f,.45f,.36f,iron,12);
                Flame(turret,new(0,height+.38f,0),1.4f,x+z);
            }
            else
            {
                Cone(turret,new(0,height+.6f,0),radius+.16f,1.2f,slate,16);
                Pennant(turret,new(0,height+1.15f,0),.9f,new("c2263a"),x*.7f+z);
            }
        }

        // Banners on the rear wall's inner face, between the recessed columns and well
        // above the upper deck's standing space.
        foreach(float x in new[]{5.5f,9.5f,13.5f})
        {
            var banner=Prop(scenery,"WallBanner",new(x,4.75f,1.012f));
            Box(banner,new(0,.08f,0),new(1.02f,.08f,.06f),gold,.01f);
            Plate(banner,new(0,0,.012f),[new(-.42f,0),new(.42f,0),new(.42f,-1.45f),new(0,-1.72f),new(-.42f,-1.45f)],.02f,crimson);
            Plate(banner,new(0,0,.026f),[new(-.36f,-.06f),new(.36f,-.06f),new(.36f,-1.38f),new(0,-1.6f),new(-.36f,-1.38f)],.01f,gold);
            Plate(banner,new(0,0,.034f),[new(-.31f,-.1f),new(.31f,-.1f),new(.31f,-1.34f),new(0,-1.53f),new(-.31f,-1.34f)],.01f,crimson);
            // An original bomb emblem: round body, cap and a lit fuse.
            // Kept shallow: the banner may project only a few centimetres from the wall face.
            Sphere(banner,new(0,-.86f,.045f),new(.42f,.42f,.03f),iron);
            Box(banner,new(0,-.6f,.045f),new(.14f,.08f,.03f),gold);
            Box(banner,new(.06f,-.5f,.045f),new(.03f,.14f,.02f),_art.Color(new("e2d6b4"))).Rotation=new(0,0,-.5f);
            Sphere(banner,new(.11f,-.42f,.05f),new(.08f,.08f,.03f),Glow(new("ffcf5a"),2));
        }

        // Cloud banks drift through the void below the citadel.
        Cloud(scenery,new(-6,-4.2f,4),1.3f,.35f,40,0);
        Cloud(scenery,new(Width+6,-5,12),1.5f,.28f,44,13);
        Cloud(scenery,new(Width/2f,-6.2f,Depth+4),1.7f,.22f,50,27);
        Cloud(scenery,new(Width/2f,-3.4f,-5.5f),1.2f,.3f,44,6);
        Cloud(scenery,new(-4,-7.5f,Depth-2),1.4f,.25f,46,33);

        // Floating islets with tiny watchtowers bob slowly around the fortress.
        foreach(var (at,scale,phase) in new[]{(new Vector3(-6.4f,1.5f,-2.5f),1f,0f),(new Vector3(Width+6.6f,.4f,4.5f),.8f,2f),(new Vector3(-7,-1.6f,13),.7f,4f)})
        {
            var islet=Prop(scenery,"FloatingIslet",at,phase,scale);
            Cone(islet,new(0,-1.2f,0),1.3f,2.4f,rock,7).Rotation=new(Mathf.Pi,0,0);
            Cylinder(islet,new(0,.02f,0),1.3f,.18f,_art.Color(new("5d8a55"),.9f));
            Cylinder(islet,new(.2f,.75f,0),.38f,1.3f,stone);
            Cone(islet,new(.2f,1.65f,0),.5f,.6f,slate,12);
            Flame(islet,new(-.65f,.12f,.4f),.55f,phase);
            float y=at.Y;
            _sceneryMotion.Add(t=>islet.Position=new(at.X,y+Mathf.Sin(t*.45f+phase)*.25f,at.Z));
        }

        Starfield(scenery,new(Width/2f,-10,Depth/2f),120,30,520,17);
        BuildOrbitalBackdrop();
    }

    /// <summary>Layered irregular rings narrowing to a point well below the foundation.</summary>
    private static ArrayMesh CragMesh(int width,int depth)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=28;
        float[] levels=[-.9f,-2.4f,-4.6f,-7.2f,-10.5f];
        float[] scales=[1.04f,.86f,.6f,.32f,.06f];
        var center=new Vector3(width/2f,0,depth/2f);
        Vector3 Point(int layer,int i)
        {
            float a=i*Mathf.Tau/segments;
            float wobble=layer==0?0:.12f*Mathf.Sin(i*2.7f+layer*1.9f)+.06f*Mathf.Sin(i*5.3f+layer);
            // A rounded rectangle at the top relaxes into a jagged cone.
            var dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            float square=1/Mathf.Max(Mathf.Abs(dir.X),Mathf.Abs(dir.Y));
            float shape=Mathf.Lerp(square,1.1f,layer/(float)(levels.Length-1));
            float s=scales[layer]*(1+wobble)*shape;
            return center+new Vector3(dir.X*width/2f*s,levels[layer],dir.Y*depth/2f*s);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(c-a).Cross(b-a).Normalized();
            var mid=(a+b+c)/3;
            if(normal.Dot(new Vector3(mid.X-center.X,0,mid.Z-center.Z))<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X+p.Z,p.Y));surface.AddVertex(p);}
        }
        for(int layer=0;layer<levels.Length-1;layer++)for(int i=0;i<segments;i++)
        {
            var a=Point(layer,i);var b=Point(layer,(i+1)%segments);
            var c=Point(layer+1,(i+1)%segments);var d=Point(layer+1,i);
            Triangle(a,b,c);Triangle(a,c,d);
        }
        return surface.Commit();
    }
}
