using Godot;
using Bomber.Game.Simulation;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena;

public partial class TempleStageView
{
    private void BuildRockGarden()
    {
        Name="RockGardenTerrain";
        var grass=_art.GardenSurface(grass:true);
        var stone=_art.GardenSurface();
        _gardenRockMaterial=_art.GardenSurface(boulder:true);
        _gardenMaterials.AddRange([grass,stone,(ShaderMaterial)_gardenRockMaterial]);
        // The exterior is scenery below the playable floor. Only authority cells
        // inside the stone enclosure support players; there is no invented route.
        Box(this,new(Width/2f,-.38f,Depth/2f),new(160,.28f,160),grass);
        Box(this,new(Width/2f,-.15f,Depth/2f),new(Width,.3f,Depth),grass);
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
                    Box(this,new(x+.5f,(height-.08f)/2,z+.5f),new(.995f,height-.08f,.995f),stone,.035f).Name="EnclosingWall";
                    Box(this,new(x+.5f,height-.04f,z+.5f),new(.998f,.08f,.998f),stone,.02f);
                }
                else GardenColumn(x,z,stone);
            }
            if(Math.Abs(x-Width/2)+Math.Abs(z-Depth/2)<=3)
                Box(paving,new(x+.5f,-.013f,z+.5f),new(.96f,.05f,.96f),stone,.018f);
        }
        _gardenRocks=Enumerable.Range(0,4).Select(GardenRockMesh).ToArray();
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
        const int sides=7;
        float height=BombThrowRules.CoverHeight*HeightScale;
        var rings=new Vector3[3,sides];
        for(int ring=0;ring<3;ring++)for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.Tau/sides;
            float radius=(ring==0?.43f:ring==1?.47f:.28f)*(1+.06f*Mathf.Sin(i*3.1f+variant));
            float y=ring==0?.015f:ring==1?height*.58f:height*(.92f+.08f*Mathf.Sin(i*2.7f+variant));
            rings[ring,i]=new(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
        }
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(c-a).Cross(b-a).Normalized();
            if(normal.Dot((a+b+c)/3-new Vector3(0,height*.4f,0))<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
        }
        for(int i=0;i<sides;i++)
        {
            int next=(i+1)%sides;
            for(int ring=0;ring<2;ring++)
            {
                Triangle(rings[ring,i],rings[ring,next],rings[ring+1,next]);
                Triangle(rings[ring,i],rings[ring+1,next],rings[ring+1,i]);
            }
            Triangle(rings[2,i],rings[2,next],new(.025f,height,0));
        }
        return surface.Commit();
    }
}
