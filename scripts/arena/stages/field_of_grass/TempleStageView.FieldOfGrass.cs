using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildFieldOfGrass()
    {
        Name="FieldOfGrassTerrain";
        var turf=_art.GardenSurface(grass:true);
        turf.SetShaderParameter("grass_dark",new Color("477735"));
        turf.SetShaderParameter("grass_light",new Color("87a94e"));
        var bank=_art.GardenSurface(boulder:true);
        bank.SetShaderParameter("stone_color",new Color("b48d60"));
        _fieldMaterials.AddRange([turf,bank]);
        Box(this,new(Width/2f,-.43f,Depth/2f),new(160,.3f,160),turf).Name="FieldExterior";
        Box(this,new(Width/2f,-.17f,Depth/2f),new(Width,.34f,Depth),bank).Name="MeadowFoundation";
        var timber=_art.Toy(new("ad9167"),.8f);
        var rail=_art.Toy(new("dec293"),.85f);
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);
            if(cell.Kind==CellKind.Pillar)
            {
                float baseY=Layout.SurfaceAt(Math.Clamp(x+.5f,1.5f,Width-1.5f),Math.Clamp(z+.5f,1.5f,Depth-1.5f))*HeightScale;
                // A low near fence leaves the meadow and every character readable.
                Box(this,new(x+.5f,baseY+.3f,z+.5f),new(.16f,.6f,.16f),timber,.025f).Name="MeadowFencePost";
                bool side=x==0||x==Width-1;
                foreach(float y in new[]{.22f,.48f})
                    Box(this,new(x+.5f,baseY+y,z+.5f),side?new(.095f,.085f,1):new(1,.085f,.095f),rail,.012f);
                continue;
            }
            if(cell.Kind==CellKind.Ramp)
            {
                float y0=(cell.RampDX<0?cell.RampToTier:cell.RampFromTier)*HeightScale,y1=(cell.RampDX<0?cell.RampFromTier:cell.RampToTier)*HeightScale;
                var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
                var a=new Vector3(x,y0,z);var b=new Vector3(x+1,y1,z);var c=new Vector3(x+1,y1,z+1);var d=new Vector3(x,y0,z+1);
                var normal=(d-a).Cross(b-a).Normalized();
                foreach(var p in new[]{a,c,b,a,d,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
                surface.Index();AddChild(new MeshInstance3D{Name="MeadowBankSlope",Mesh=surface.Commit(),MaterialOverride=bank});
                // Close the outer edge beneath the slope; the support never changes movement.
                if(z==1||z==Depth-2)
                    Box(this,new(x+.5f,(Math.Min(y0,y1)-.05f)/2,z+(z==1?0:1)),new(1,Math.Min(y0,y1)+.05f,.05f),bank);
            }
            else
            {
                float y=cell.Tier*HeightScale;
                Box(this,new(x+.5f,(y-.12f)/2,z+.5f),new(1,y+.12f,1),x<7||x>=14?bank:turf);
                if(cell.Tier==2)
                    Box(this,new(x+.5f,y+.012f,z+.5f),new(.98f,.024f,.98f),turf,.015f);
            }
        }
        // Original woolly seed-head meadow: the opaque leaves hide lower bodies and
        // normal bomb shells, while heads and pumped bombs rise above the canopy.
        BuildFieldGrass();
    }

    private void ProcessField(float delta)
    {
        if(WeatherActive)_fieldClock+=delta;
        foreach(var material in _fieldMaterials)
        {

            if(_graphicsRevision!=GraphicsQuality.Revision)GraphicsQuality.ApplyShaderDetail(material);
        }
        _fieldGrassMaterial?.SetShaderParameter("ambient_clock",_fieldClock);
        _graphicsRevision=GraphicsQuality.Revision;
    }

    private Node3D CreateFieldBoulder(Node3D root,int cell)
    {
        root.Name="MeadowBoulder"+cell;
        var paint=_art.Color(new("a5a18b"),.9f);
        // The same bounded support/height as shared breakable cover; original soft rocks.
        if(_gardenRocks.Length==0)_gardenRocks=Enumerable.Range(0,4).Select(GardenRockMesh).ToArray();
        root.AddChild(new MeshInstance3D{Mesh=_gardenRocks[cell%4],MaterialOverride=paint,Scale=new(1,Layout.SoftBlockHeight/Bomber.Game.Simulation.Rules.BombThrowRules.CoverHeight,1)});
        root.Rotation=new(0,(cell*2.39996f)%Mathf.Tau,0);
        return root;
    }

    private void BuildFieldGrass()
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        // Broad folded leaves, a raised horizontal crown and soft pointed tips.
        // One reusable mesh + one MultiMesh keep the dense canopy a single draw.
        for(int blade=0;blade<4;blade++)
        {
            float angle=blade*Mathf.Tau/4;
            Vector3 P(float w,float y,float bend)=>new(Mathf.Cos(angle)*w+Mathf.Sin(angle)*bend,y,Mathf.Sin(angle)*w-Mathf.Cos(angle)*bend);
            void V(float w,float y,float bend){surface.SetNormal(new Vector3(Mathf.Sin(angle),.6f,-Mathf.Cos(angle)).Normalized());surface.SetUV(new(w+.5f,y));surface.AddVertex(P(w,y,bend));}
            V(-.22f,0,0);V(.17f,.88f,.14f);V(.22f,0,0);
            V(-.22f,0,0);V(-.17f,.88f,.14f);V(.17f,.88f,.14f);
            V(-.17f,.88f,.14f);V(.035f,1.12f,.3f);V(.17f,.88f,.14f);
        }
        // Leafy canopy closes gaps without adding a collision surface.
        foreach(var p in new[]{new Vector3(-.32f,.88f,-.32f),new Vector3(.32f,.88f,.32f),new Vector3(.32f,.88f,-.32f),new Vector3(-.32f,.88f,-.32f),new Vector3(-.32f,.88f,.32f),new Vector3(.32f,.88f,.32f)})
        {surface.SetNormal(Vector3.Up);surface.SetUV(new(p.X+.5f,.88f));surface.AddVertex(p);}
        surface.Index();var mesh=surface.Commit();
        const int columns=20,rows=47,count=columns*rows;
        var grass=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=count};
        var buffer=new float[count*MultiMeshPacking.Stride];var random=new Random(6103);
        for(int i=0;i<count;i++)
        {
            float x=7.12f+(i%columns)*.35f,z=1.15f+(i/columns)*.355f;
            float h=.95f+(float)random.NextDouble()*.13f;
            var basis=Basis.Identity.Scaled(new(.99f,h,.99f));
            MultiMeshPacking.Write(buffer,i,new(basis,new(x,0,z)),new Color(.84f+(float)random.NextDouble()*.16f,1,.83f+(float)random.NextDouble()*.12f));
        }
        grass.Buffer=buffer;
        var material=_art.Foliage(new("84a848"),true);_fieldGrassMaterial=material;
        AddChild(new MultiMeshInstance3D{Name="ConcealingMeadow",Multimesh=grass,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.4f});
    }
}
