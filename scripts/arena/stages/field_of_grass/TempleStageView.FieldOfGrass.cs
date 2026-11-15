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
                // Posts and rails follow the actual continuous bank profile.
                // No rectangular stair fillers project in front of the slopes.
                Box(this,new(x+.5f,baseY+.3f,z+.5f),new(.12f,.6f,.12f),timber,.025f).Name="MeadowFencePost";
                bool side=x==0||x==Width-1;
                var below=Layout.At(x,1);
                float yLeft=below.Kind==CellKind.Ramp?(below.RampDX<0?below.RampToTier:below.RampFromTier)*HeightScale:baseY;
                float yRight=below.Kind==CellKind.Ramp?(below.RampDX<0?below.RampFromTier:below.RampToTier)*HeightScale:baseY;
                float rise=yRight-yLeft,length=MathF.Sqrt(1+rise*rise),inverse=1/length;
                foreach(float y in new[]{.22f,.48f})
                {
                    var bar=Box(this,new(x+.5f,(yLeft+yRight)/2+y,z+.5f),side?new(.075f,.07f,1):new(length,.07f,.075f),rail,.012f);
                    if(!side)bar.Basis=new(new(inverse,rise*inverse,0),new(-rise*inverse,inverse,0),Vector3.Back);
                }
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
                // A continuous triangular earth face closes the diorama edge.
                if(z==1||z==Depth-2)
                {
                    float edge=z==1?z:z+1;bool reverse=z==1;
                    var face=new SurfaceTool();face.Begin(Mesh.PrimitiveType.Triangles);
                    var lowA=new Vector3(x,-.17f,edge);var lowB=new Vector3(x+1,-.17f,edge);
                    var highA=new Vector3(x,y0,edge);var highB=new Vector3(x+1,y1,edge);
                    foreach(var point in reverse?new[]{lowA,highB,highA,lowA,lowB,highB}:new[]{lowA,highA,highB,lowA,highB,lowB})
                    {face.SetNormal(reverse?Vector3.Forward:Vector3.Back);face.SetUV(new(point.X,point.Y));face.AddVertex(point);}
                    face.Index();AddChild(new MeshInstance3D{Name="ContinuousBankEdge",Mesh=face.Commit(),MaterialOverride=bank});
                }
            }
            else
            {
                float y=cell.Tier*HeightScale;
                Box(this,new(x+.5f,(y-.12f)/2,z+.5f),new(1,y+.12f,1),x<5||x>=16?bank:turf);
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
        // An asymmetric tuft of five curved, folded ribbon leaves. Each leaf
        // arches over its neighbours rather than closing into a pointed pyramid.
        for(int blade=0;blade<5;blade++)
        {
            float angle=blade*2.39996f+.23f,height=.86f+(blade%3)*.13f;
            var (sin,cos)=PresentationMath.SinCos(angle);
            float width=.09f+(blade%2)*.025f,bend=.36f+(blade%3)*.07f;
            Vector3 P(float side,float t)
            {
                float w=width*(1-t*t)*side;
                float reach=bend*t*t;
                return new(cos*w+sin*reach,height*t-.10f*t*t*t,sin*w-cos*reach);
            }
            void V(float side,float t)
            {
                surface.SetNormal(new Vector3(sin,.55f+Math.Abs(side)*.2f,-cos).Normalized());
                surface.SetUV(new((side+1)/2,t));surface.AddVertex(P(side,t));
            }
            for(int segment=0;segment<3;segment++)
            {
                float a=segment/3f,b=(segment+1)/3f;
                V(-1,a);V(1,b);V(1,a);V(-1,a);V(-1,b);V(1,b);
            }
        }
        surface.Index();var mesh=surface.Commit();
        const int columns=33,rows=51,count=columns*rows;
        var grass=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=count};
        var buffer=new float[count*MultiMeshPacking.Stride];var random=new Random(6103);
        for(int i=0;i<count;i++)
        {
            // Jittered staggered roots preserve dense concealment while removing
            // parallel rows. Independent yaw/width/height and broad colour patches
            // make neighbouring tufts read as one living, wind-combed meadow.
            float x=5.12f+(i%columns)*.333f+((i/columns)%2)*.08f+((float)random.NextDouble()-.5f)*.24f;
            float z=1.12f+(i/columns)*.333f+((float)random.NextDouble()-.5f)*.25f;
            float h=.91f+(float)random.NextDouble()*.32f;
            float spread=.86f+(float)random.NextDouble()*.42f;
            var (sine,cosine)=PresentationMath.SinCos((float)random.NextDouble()*Mathf.Tau);
            var basis=new Basis(cosine,0,sine,0,1,0,-sine,0,cosine).Scaled(new(spread,h,spread));
            float patch=.5f+.5f*PresentationMath.Sin(x*.61f+z*.37f);
            var tint=new Color(.82f+patch*.14f+(float)random.NextDouble()*.09f,.94f+(float)random.NextDouble()*.06f,.79f+patch*.16f);
            MultiMeshPacking.Write(buffer,i,new(basis,new(x,Surface(x,z),z)),tint);
        }
        grass.Buffer=buffer;
        var material=_art.Foliage(new("96b766"),true);_fieldGrassMaterial=material;
        AddChild(new MultiMeshInstance3D{Name="ConcealingMeadow",Multimesh=grass,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.4f});
    }
}
