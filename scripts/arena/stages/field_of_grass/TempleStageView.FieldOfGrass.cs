using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private ShaderMaterial? _fieldSeedPaint;
    private void BuildFieldOfGrass()=>BuildCutawayHollow();

    private void ProcessField(float delta)
    {
        if(WeatherActive)_fieldClock+=delta;
        if(_graphicsRevision!=GraphicsQuality.Revision)ApplyHollowLanternLighting(_lighting);
        foreach(var material in _fieldMaterials)
        {
            material.SetShaderParameter("ambient_clock",_fieldClock);
            if(_graphicsRevision!=GraphicsQuality.Revision)GraphicsQuality.ApplyShaderDetail(material);
        }
        _fieldGrassMaterial?.SetShaderParameter("ambient_clock",_fieldClock);
        _fieldSeedPaint?.SetShaderParameter("ambient_clock",_fieldClock);
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
        const int columns=33,rows=51;
        var random=new Random(6103);var plants=new List<(Vector3 Position,Basis Basis,Color Tint)>();
        for(int i=0;i<columns*rows;i++)
        {
            float z=.95f+(i/columns)*.344f+((float)random.NextDouble()-.5f)*.25f;
            float left=5.0f+PresentationMath.Sin(z*.47f+.8f)*.60f;
            float right=16.0f+PresentationMath.Sin(z*.41f+2)*.60f;
            float u=((i%columns)+.5f+((float)random.NextDouble()-.5f)*.55f)/columns;
            float x=Mathf.Lerp(left,right,u);
            float front=1.0f+PresentationMath.Sin(x*.61f+.8f)*.35f;
            float rear=Depth-1.0f+PresentationMath.Sin(x*.57f+1.2f)*.35f;
            float fade=HollowSmooth(front,front+.8f,z)*(1-HollowSmooth(rear-.8f,rear,z));
            if(random.NextDouble()>fade||x<4.5f||x>16.5f)continue;
            bool shortTuft=x<5||x>16;
            if(shortTuft&&random.NextDouble()>.25)continue;
            float h=shortTuft?.15f+(float)random.NextDouble()*.15f:(.91f+(float)random.NextDouble()*.32f)*(.28f+.72f*fade);
            float spread=shortTuft?.5f:.86f+(float)random.NextDouble()*.42f;
            var(sine,cosine)=PresentationMath.SinCos((float)random.NextDouble()*Mathf.Tau);
            var basis=new Basis(cosine,0,sine,0,1,0,-sine,0,cosine).Scaled(new(spread,h,spread));
            float patch=.5f+.5f*PresentationMath.Sin(x*.61f+z*.37f);
            var tint=new Color(.86f+patch*.1f,.96f,.82f+patch*.12f,shortTuft?.28f:1);
            plants.Add((new(x,Surface(x,z),z),basis,tint));
        }
        var grass=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=plants.Count};
        var buffer=new float[plants.Count*MultiMeshPacking.Stride];
        for(int i=0;i<plants.Count;i++)MultiMeshPacking.Write(buffer,i,new(plants[i].Basis,plants[i].Position),plants[i].Tint);
        grass.Buffer=buffer;
        var material=_art.HollowGrass();_fieldGrassMaterial=material;
        AddChild(new MultiMeshInstance3D{Name="ConcealingMeadow",Multimesh=grass,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.4f});
        BuildHollowSeedHeads();
    }

    private void BuildHollowSeedHeads()
    {
        var ear=new SurfaceTool();ear.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        {
            var normal=(c-a).Cross(b-a).Normalized();if(normal.Dot(outward)<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){ear.SetNormal(normal);ear.SetUV(new(p.X,p.Y/1.4f));ear.AddVertex(p);}
        }
        foreach(var(offset,height)in new[]{(-.13f,1.12f),(.04f,1.29f),(.19f,1.04f)})
        {
            var a=new Vector3(offset-.018f,.02f,0);var b=new Vector3(offset+.018f,.02f,0);
            var c=new Vector3(offset+.018f,height-.13f,0);var d=new Vector3(offset-.018f,height-.13f,0);
            Triangle(a,b,c,Vector3.Back);Triangle(a,c,d,Vector3.Back);
            const int sides=6;
            for(int i=0;i<sides;i++)
            {
                var(s0,c0)=PresentationMath.SinCos(i*Mathf.Tau/sides);var(s1,c1)=PresentationMath.SinCos((i+1)*Mathf.Tau/sides);
                var p=new Vector3(offset+c0*.075f,height-.08f,s0*.075f);var q=new Vector3(offset+c1*.075f,height-.08f,s1*.075f);
                Triangle(new(offset,height-.23f,0),q,p,new(c0,-.1f,s0));
                Triangle(p,q,new(offset+.025f,height+.09f,0),new(c0,.1f,s0));
            }
        }
        ear.Index();var mesh=ear.Commit();
        var positions=new List<Vector3>();
        foreach(float x in new[]{6.35f,14.65f})foreach(float z in new[]{2.8f,5.3f,7.6f,10.8f,13.7f,16.3f})positions.Add(new(x,0,z));
        foreach(float x in new[]{8.5f,12.5f})foreach(float z in new[]{4.4f,14.7f})positions.Add(new(x,0,z));
        var multimesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=positions.Count};
        var buffer=new float[positions.Count*MultiMeshPacking.Stride];
        for(int i=0;i<positions.Count;i++)
            MultiMeshPacking.Write(buffer,i,new(PresentationMath.Rotation(new(0,i*2.39996f,0)),positions[i]),Colors.White);
        multimesh.Buffer=buffer;
        _fieldSeedPaint=_art.HollowGrass();_fieldSeedPaint.SetShaderParameter("paint",new Color("929052"));
        _fieldSeedPaint.SetShaderParameter("tip_paint",new Color("baa56a"));
        AddChild(new MultiMeshInstance3D{Name="HollowSeedHeads",Multimesh=multimesh,MaterialOverride=_fieldSeedPaint,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.4f});
    }
}
