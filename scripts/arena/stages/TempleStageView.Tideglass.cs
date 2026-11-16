using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D? _coastalBeacon,_coastalBell;
    private StandardMaterial3D? _coastalLens;
    private float _coastalLifeClock;

    private void ProcessCoastalLife(float delta)
    {
        if(_coastalBeacon==null)return;
        if(WeatherActive)_coastalLifeClock+=delta;
        _coastalBeacon.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
        _coastalBeacon.Basis=PresentationMath.Rotation(new(0,_coastalLifeClock*.22f,0));
        if(_coastalLens!=null)_coastalLens.EmissionEnergyMultiplier=_lighting?.Label=="Night"?
            1.05f+.15f*PresentationMath.Sin(_coastalLifeClock*.8f):.7f;
        if(_coastalBell!=null)
        {
            _coastalBell.Position=new(_coastalBell.Position.X,SceneryWaterHeight+.06f*PresentationMath.Sin(_coastalLifeClock*.7f),_coastalBell.Position.Z);
            _coastalBell.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(_coastalLifeClock*.85f)*.055f));
        }
    }

    private void BuildCoastalBellBuoy(Vector3 position)
    {
        _coastalBell=Prop(this,"TideglassBellBuoy",position);
        var cream=_art.Toy(new("e9dfc8"),.72f);var red=_art.Toy(new("b95343"),.65f);
        var iron=_art.Toy(new("56746f"),.72f);var brass=_art.Toy(new("c49c5e"),.55f);
        Sphere(_coastalBell,new(0,.04f,0),new(.8f,.25f,.8f),red);
        Sphere(_coastalBell,new(0,.10f,0),new(.72f,.14f,.72f),cream);
        foreach(int side in new[]{-1,1})RoundedBox(_coastalBell,new(side*.25f,.70f,0),new(.065f,1.1f,.065f),iron,.6f);
        RoundedBox(_coastalBell,new(0,1.24f,0),new(.61f,.08f,.09f),iron,.6f);
        RoundedBox(_coastalBell,new(0,1.18f,0),new(.055f,.10f,.055f),brass,.4f);
        _coastalBell.AddChild(new MeshInstance3D{Name="CoastalBell",Mesh=CoastalBellMesh(),Position=new(0,.59f,0),MaterialOverride=brass});
        Sphere(_coastalBell,new(0,.64f,0),Vector3.One*.10f,brass);
    }

    private static void CoastalTriangle(SurfaceTool tool,Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc,Color paint)
    {
        var cross=(b-a).Cross(c-a);if(cross.LengthSquared()<1e-10f)return;
        if(cross.Dot(na+nb+nc)>0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
        foreach(var(p,n) in new[]{(a,na),(b,nb),(c,nc)})
        {tool.SetNormal(n);tool.SetColor(paint);tool.SetUV(new(p.X,p.Y));tool.AddVertex(p);}
    }

    private static ArrayMesh CoastalBellMesh()
    {
        (float R,float Y)[] profile=[(0,.56f),(.09f,.52f),(.16f,.40f),(.21f,.16f),(.29f,.065f),(.30f,0),
            (.25f,0),(.19f,.11f),(.12f,.36f),(0,.44f)];
        const int sides=20;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N) At(int ring,int side)
        {
            var(sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
            var p=profile[ring];var before=profile[Math.Max(0,ring-1)];var after=profile[Math.Min(profile.Length-1,ring+1)];
            var n=p.R==0?(ring==0?Vector3.Up:Vector3.Down):new Vector3(cos*(before.Y-after.Y),after.R-before.R,sin*(before.Y-after.Y)).Normalized();
            return (new(cos*p.R,p.Y,sin*p.R),n);
        }
        for(int ring=0;ring<profile.Length-1;ring++)for(int side=0;side<sides;side++)
        {
            var a=At(ring,side);var b=At(ring,side+1);var c=At(ring+1,side+1);var d=At(ring+1,side);
            CoastalTriangle(surface,a.P,b.P,c.P,a.N,b.N,c.N,Colors.White);CoastalTriangle(surface,a.P,c.P,d.P,a.N,c.N,d.N,Colors.White);
        }
        surface.Index();return surface.Commit();
    }

    private static ArrayMesh CoastalArchMesh(float height,float width)
    {
        const int sides=32;float[] depth=[-.95f,-.72f,.72f,.95f];float[] outerScale=[.90f,1,1,.90f],innerScale=[1,1.06f,1.06f,1];
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int section,float angle,bool inner)
        {
            var(sin,cos)=PresentationMath.SinCos(angle);
            if(inner)return new(cos*width*.26f*innerScale[section],1.05f+sin*1.3f*innerScale[section],depth[section]);
            float x=(cos<0?-1:1)*MathF.Sqrt(Math.Abs(cos))*width*.52f*outerScale[section];
            float y=(height-1)/2+(sin<0?-1:1)*MathF.Sqrt(Math.Abs(sin))*(height+1)/2*outerScale[section];
            return new(x,y,depth[section]);
        }
        Vector3 Normal(int section,float angle,bool inner)
        {
            var across=Point(section,angle+.001f,inner)-Point(section,angle-.001f,inner);
            var along=Point(Math.Min(3,section+1),angle,inner)-Point(Math.Max(0,section-1),angle,inner);
            var n=across.Cross(along).Normalized();return inner?-n:n;
        }
        Color Paint(Vector3 p,bool inner)
        {
            if(inner)return new("657e79");
            if(p.Y>height-.35f)return new("78995e");
            return p.Y<.25f?new("647e75"):new Color("939e8f")*(1+.025f*PresentationMath.Sin(p.Y*1.2f+p.X));
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc,bool inner)
        {
            var cross=(b-a).Cross(c-a);if(cross.Dot(na+nb+nc)>0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
            foreach(var(p,n) in new[]{(a,na),(b,nb),(c,nc)}){surface.SetNormal(n);surface.SetColor(Paint(p,inner));surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
        }
        foreach(bool inner in new[]{false,true})for(int section=0;section<3;section++)for(int side=0;side<sides;side++)
        {
            float angle=side*Mathf.Tau/sides,next=(side+1)*Mathf.Tau/sides;
            var a=Point(section,angle,inner);var b=Point(section+1,angle,inner);var c=Point(section+1,next,inner);var d=Point(section,next,inner);
            Triangle(a,b,c,Normal(section,angle,inner),Normal(section+1,angle,inner),Normal(section+1,next,inner),inner);
            Triangle(a,c,d,Normal(section,angle,inner),Normal(section+1,next,inner),Normal(section,next,inner),inner);
        }
        foreach(int section in new[]{0,3})for(int side=0;side<sides;side++)
        {
            float angle=side*Mathf.Tau/sides,next=(side+1)*Mathf.Tau/sides;
            var a=Point(section,angle,false);var b=Point(section,next,false);var c=Point(section,next,true);var d=Point(section,angle,true);
            var n=section==0?Vector3.Forward:Vector3.Back;
            Triangle(a,b,c,n,n,n,false);Triangle(a,c,d,n,n,n,false);
        }
        surface.Index();return surface.Commit();
    }
}
