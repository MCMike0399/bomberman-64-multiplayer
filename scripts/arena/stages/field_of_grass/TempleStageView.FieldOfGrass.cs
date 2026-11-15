using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildFieldOfGrass()=>BuildCutawayHollow();

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
                float rise=height-.3f*t*t;
                surface.SetNormal(new Vector3(-sin*rise,2*bend*t,cos*rise).Normalized());
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
            float z=.95f+(i/columns)*.344f+((float)random.NextDouble()-.5f)*.25f;
            float left=4.72f+PresentationMath.Sin(z*.72f+.8f)*.31f+PresentationMath.Sin(z*1.7f)*.11f;
            float right=16.28f+PresentationMath.Sin(z*.51f+2)*.30f;
            float u=((i%columns)+.5f+((float)random.NextDouble()-.5f)*.55f)/columns;
            float x=Mathf.Lerp(left,right,u);
            z+=PresentationMath.Sin(x*.6f+1.2f)*.11f;
            float h=.91f+(float)random.NextDouble()*.32f;
            float spread=.86f+(float)random.NextDouble()*.42f;
            var (sine,cosine)=PresentationMath.SinCos((float)random.NextDouble()*Mathf.Tau);
            var basis=new Basis(cosine,0,sine,0,1,0,-sine,0,cosine).Scaled(new(spread,h,spread));
            float patch=.5f+.5f*PresentationMath.Sin(x*.61f+z*.37f);
            var tint=new Color(.82f+patch*.14f+(float)random.NextDouble()*.09f,.94f+(float)random.NextDouble()*.06f,.79f+patch*.16f);
            MultiMeshPacking.Write(buffer,i,new(basis,new(x,Surface(x,z),z)),tint);
        }
        grass.Buffer=buffer;
        var material=_art.HollowGrass();_fieldGrassMaterial=material;
        AddChild(new MultiMeshInstance3D{Name="ConcealingMeadow",Multimesh=grass,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.4f});
    }
}
