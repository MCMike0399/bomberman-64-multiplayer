using Godot;
using BlastCircuit.Rendering;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildRockGarden()
    {
        Name="RockGardenTerrain";
        var grass=_art.GardenSurface(grass:true);
        grass.SetShaderParameter("grass_dark",new Color("4a7139"));
        grass.SetShaderParameter("grass_light",new Color("5e8444"));
        grass.SetShaderParameter("grass_variation",.3f);
        var outer=_art.GardenSurface(grass:true);
        outer.SetShaderParameter("grass_dark",new Color("31543a"));
        outer.SetShaderParameter("grass_light",new Color("3a6040"));
        outer.SetShaderParameter("grass_variation",.12f);
        var mown=_art.GardenSurface(grass:true);
        mown.SetShaderParameter("grass_dark",new Color("708e51"));
        mown.SetShaderParameter("grass_light",new Color("7a9a59"));
        mown.SetShaderParameter("grass_variation",.08f);
        var stone=_art.GardenSurface();
        var lichened=_art.GardenSurface();lichened.SetShaderParameter("lichen_patch",.7f);
        var coping=_art.Toy(new Color("e2d6b4"),.86f);
        _gardenRockMaterial=_art.GardenSurface(boulder:true);
        _gardenMaterials.AddRange([grass,outer,mown,stone,lichened,(ShaderMaterial)_gardenRockMaterial]);
        // The exterior is scenery below the playable floor. Only authority cells
        // inside the stone enclosure support players; there is no invented route.
        Box(this,new(Width/2f,-.38f,Depth/2f),new(160,.28f,160),outer).Name="GardenExterior";
        foreach(float x in new[]{-.7f,Width+.7f})
            Box(this,new(x,-.235f,Depth/2f),new(1.4f,.03f,Depth+2.8f),mown).Name=x<0?"GardenMownBorderWest":"GardenMownBorderEast";
        foreach(float z in new[]{-.7f,Depth+.7f})
            Box(this,new(Width/2f,-.235f,z),new(Width,.03f,1.4f),mown).Name=z<0?"GardenMownBorderNorth":"GardenMownBorderSouth";
        Box(this,new(Width/2f,-.15f,Depth/2f),new(Width,.3f,Depth),grass);
        float crown=BombThrowRules.PillarHeight*HeightScale;
        foreach(float x in new[]{.5f,Width-.5f})
            GardenCoping(new(x,crown-.22f,Depth/2f),Depth,Mathf.Pi/2,coping);
        foreach(float z in new[]{.5f,Depth-.5f})
            GardenCoping(new(Width/2f,crown-.22f,z),Width-2,0,coping);
        var paving=new Node3D{Name="CentralPaving"};AddChild(paving);
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=_cells[z*Width+x];
            if(cell.GetProperty("k").GetInt32()==(int)CellKind.Pillar)
            {
                bool boundary=x==0||z==0||x==Width-1||z==Depth-1;
                if(boundary)
                {
                    float height=BombThrowRules.PillarHeight*HeightScale;
                    bool patch=x==0&&z is >=4 and <=6 || z==0&&x is >=10 and <=12 || x==Width-1&&z is >=13 and <=15;
                    Box(this,new(x+.5f,(height-.22f)/2,z+.5f),new(.995f,height-.22f,.995f),patch?lichened:stone,.045f).Name="EnclosingWall";
                }
                else GardenColumn(x,z,stone);
            }
            if(Math.Abs(x-Width/2)+Math.Abs(z-Depth/2)<=3)
                Box(paving,new(x+.5f,-.013f,z+.5f),new(.96f,.05f,.96f),stone,.018f);
        }
        _gardenRocks=Enumerable.Range(0,4).Select(GardenRockMesh).ToArray();
    }

    private void GardenCoping(Vector3 position,float length,float yaw,Material paint)
    {
        const int sides=12;const float radius=.499f,rise=.22f;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N) At(float x,int step)
        {
            var (sin,cos)=PresentationMath.SinCos(step*Mathf.Pi/sides);
            return (new(x,sin*rise,cos*radius),new Vector3(0,sin/rise,cos/radius).Normalized());
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc)
        {
            if((b-a).Cross(c-a).Dot(na+nb+nc)>0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
            foreach(var v in new[]{(a,na),(b,nb),(c,nc)}){surface.SetNormal(v.Item2);surface.SetUV(new(v.Item1.X,v.Item1.Z));surface.AddVertex(v.Item1);}
        }
        for(int i=0;i<sides;i++)
        {
            var a=At(-length/2,i);var b=At(length/2,i);var c=At(length/2,i+1);var d=At(-length/2,i+1);
            Triangle(a.P,b.P,c.P,a.N,b.N,c.N);Triangle(a.P,c.P,d.P,a.N,c.N,d.N);
            Triangle(new(-length/2,0,0),a.P,d.P,Vector3.Left,Vector3.Left,Vector3.Left);
            Triangle(new(length/2,0,0),b.P,c.P,Vector3.Right,Vector3.Right,Vector3.Right);
        }
        var p0=new Vector3(-length/2,0,-radius);var p1=new Vector3(length/2,0,-radius);
        var p2=new Vector3(length/2,0,radius);var p3=new Vector3(-length/2,0,radius);
        Triangle(p0,p1,p2,Vector3.Down,Vector3.Down,Vector3.Down);Triangle(p0,p2,p3,Vector3.Down,Vector3.Down,Vector3.Down);
        surface.Index();AddChild(new MeshInstance3D{Name="GardenCreamCoping"+(yaw==0?(position.Z<Depth/2f?"North":"South"):(position.X<Width/2f?"West":"East")),Position=position,Mesh=surface.Commit(),MaterialOverride=paint,
            Basis=PresentationMath.Rotation(new(0,yaw,0))});
    }

    private void GardenColumn(int x,int z,Material stone)
    {
        var root=new Node3D{Name=$"GardenColumn{x}_{z}",Position=new(x+.5f,0,z+.5f)};AddChild(root);
        float height=(Layout.At(x,z).Tier+(Layout.At(x,z).ObstacleHeight??BombThrowRules.PillarHeight))*HeightScale;
        Cylinder(root,new(0,(height-.05f)/2,0),.49f,height-.05f,stone);
        Cylinder(root,new(0,height-.025f,0),.495f,.05f,_art.Color(new("c3b78d"),.9f));
        // A restrained dark weather line under the worn top, without temple caps.
        Cylinder(root,new(0,height-.065f,0),.492f,.018f,_art.Color(new("777d68")));
    }

    private Node3D CreateGardenBoulder(Node3D root,int cell)
    {
        root.Name="GardenBoulder"+cell;
        int variant=(cell*17+cell/Width*7)%_gardenRocks.Length;
        root.AddChild(new MeshInstance3D{Mesh=_gardenRocks[variant],MaterialOverride=_gardenRockMaterial});
        root.Rotation=new(0,(cell*2.39996f)%Mathf.Tau,0);
        return root;
    }

    private static ArrayMesh GardenRockMesh(int variant)
    {
        const int sides=20;
        float height=BombThrowRules.CoverHeight*HeightScale;
        (float Y,float Radius)[] profile=[(.015f,.40f),(height*.12f,.465f),
            (height*.32f,.49f),(height*.58f,.47f),(height*.80f,.36f),
            (height*.94f,.19f),(height,0)];
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N) At(int ring,int side)
        {
            float angle=(side%sides)*Mathf.Tau/sides;
            var (sin,cos)=PresentationMath.SinCos(angle);
            var p=profile[ring];
            float radius=p.Radius*(.985f+.015f*PresentationMath.Sin(angle*3+variant));
            var before=profile[Math.Max(0,ring-1)];var after=profile[Math.Min(profile.Length-1,ring+1)];
            var normal=p.Radius==0?Vector3.Up:new Vector3(cos*(after.Y-before.Y),before.Radius-after.Radius,sin*(after.Y-before.Y)).Normalized();
            return (new(cos*radius+.006f*PresentationMath.Sin(variant)*PresentationMath.Sin(p.Y/height*Mathf.Pi),p.Y,sin*radius),normal);
        }
        void Triangle((Vector3 P,Vector3 N) a,(Vector3 P,Vector3 N) b,(Vector3 P,Vector3 N) c)
        {
            var cross=(b.P-a.P).Cross(c.P-a.P);
            if(cross.LengthSquared()<.0000000001f)return;
            // Godot's visible front is clockwise against the outward normals.
            if(cross.Dot(a.N+b.N+c.N)>0)(b,c)=(c,b);
            foreach(var v in new[]{a,b,c}){surface.SetNormal(v.N);surface.SetUV(new(v.P.X,v.P.Z));surface.AddVertex(v.P);}
        }
        for(int ring=0;ring<profile.Length-1;ring++)for(int side=0;side<sides;side++)
        {
            var a=At(ring,side);var b=At(ring,side+1);var c=At(ring+1,side+1);var d=At(ring+1,side);
            Triangle(a,b,c);Triangle(a,c,d);
        }
        surface.Index();return surface.Commit();
    }
}
