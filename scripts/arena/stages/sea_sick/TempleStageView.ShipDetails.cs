using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildShipHull(Material timber,Material green,Material brass)
    {
        var paints=new[]{ShipPaint("466f6a"),ShipPaint("315c62"),ShipPaint("3e7773"),ShipPaint("284950")};
        float[] levels=[0,.18f,.39f,.65f,1];
        for(int band=0;band<levels.Length-1;band++)
            AddChild(new MeshInstance3D{Name="CutterHullStrake"+band,Mesh=ShipHullBand(levels[band],levels[band+1]),MaterialOverride=paints[band]});
        foreach(float t in new[]{.17f,.38f})
            AddChild(new MeshInstance3D{Name="HullBrassSeam",Mesh=ShipHullBand(t,t+.016f),MaterialOverride=brass});
        AddChild(new MeshInstance3D{Name="BowDeck",Mesh=ShipBowDeck(),MaterialOverride=timber});
        // The extended prow is scenery beyond the enclosing gunwale; its lower
        // deck is not advertised as another playable route.
        Box(this,new(9.5f,-.09f,20.4f),new(1.25f,.12f,.28f),brass,.055f);
        foreach(float x in new[]{9.05f,9.95f})
            ShipLathe(this,new(x,-.22f,20.4f),[(0,.105f),(.28f,.105f)],green,"bow-cleat");
        foreach(int side in new[]{-1,1})foreach(float z in new[]{3.4f,7.4f,11.4f,15.4f})
        {
            var port=ShipLathe(this,new(side<0?-.29f:19.29f,.66f,z),[(-.035f,.235f),(.035f,.235f)],brass,"hull-port-rim");
            port.Basis=PresentationMath.Rotation(new(0,0,side<0?Mathf.Pi/2:-Mathf.Pi/2));
            var glass=ShipLathe(this,new(side<0?-.335f:19.335f,.66f,z),[(-.009f,.162f),(.009f,.162f)],_art.Toy(new("284951"),.38f),"hull-port-glass");
            glass.Basis=port.Basis;
        }
    }

    private void BuildQuarterCabin(Material oak,Material dark,Material green,Material brass)
    {
        var cabin=new Node3D{Name="QuarterCabin",Position=new(9.5f,-.10f,-1.7f)};AddChild(cabin);
        Box(cabin,new(0,.8f,0),new(4.6f,1.6f,2.2f),green,.20f);
        Box(cabin,new(0,1.7f,0),new(5.0f,.22f,2.55f),dark,.16f);
        Box(cabin,new(0,1.84f,0),new(4.8f,.10f,2.4f),oak,.06f);
        Box(cabin,new(0,.64f,1.13f),new(.77f,1.28f,.10f),dark,.08f);
        Box(cabin,new(0,.66f,1.19f),new(.53f,1.07f,.045f),oak,.04f);
        Sphere(cabin,new(.18f,.62f,1.23f),Vector3.One*.10f,brass);
        foreach(float x in new[]{-1.5f,1.5f})
        {
            Box(cabin,new(x,.99f,1.13f),new(.86f,.72f,.09f),brass,.14f);
            Box(cabin,new(x,.99f,1.19f),new(.67f,.54f,.045f),_art.Toy(new("638e96"),.42f),.09f);
            Box(cabin,new(x,.99f,1.22f),new(.045f,.54f,.025f),brass);
        }
        // A small ship's wheel on the roof, wholly aft of the authority deck.
        ShipLathe(cabin,new(0,1.85f,0),[(0,.12f),(.50f,.10f)],dark,"helm-pedestal");
        var wheel=new Node3D{Name="Helm",Position=new(0,2.50f,.10f),Basis=PresentationMath.Rotation(new(Mathf.Pi/2,0,0))};cabin.AddChild(wheel);
        wheel.AddChild(new MeshInstance3D{Mesh=ShipAnnulus(.39f,.47f,.08f),MaterialOverride=dark});
        for(int spoke=0;spoke<8;spoke++)
        {
            float angle=spoke*Mathf.Tau/8;var (sin,cos)=PresentationMath.SinCos(angle);
            var beam=Box(wheel,new(cos*.26f,0,sin*.26f),new(.62f,.075f,.075f),oak,.025f);
            beam.Basis=PresentationMath.Rotation(new(0,-angle,0));
            Sphere(wheel,new(cos*.59f,0,sin*.59f),Vector3.One*.13f,brass);
        }
        ShipLathe(wheel,Vector3.Zero,[(-.065f,.13f),(.065f,.13f)],brass,"helm-hub");
    }

    private void BuildFurledSail(Material timber,Material rope)
    {
        var yard=ShipLathe(this,new(9.5f,6.55f,8.5f),[(-3.15f,.085f),(3.15f,.085f)],timber,"mast-yard");
        var horizontal=PresentationMath.Rotation(new(0,0,-Mathf.Pi/2));yard.Basis=horizontal;yard.Name="MastYard";
        var canvas=_art.Color(new("e3dbc6"),.94f);
        (float Y,float Radius)[] folds=[(-2.85f,.12f),(-2.68f,.22f),(-2.25f,.29f),(-1.90f,.23f),(-1.45f,.31f),(-1.05f,.24f),(-.55f,.30f),(0,.24f),(.55f,.30f),(1.05f,.24f),(1.45f,.31f),(1.90f,.23f),(2.25f,.29f),(2.68f,.22f),(2.85f,.12f)];
        var roll=ShipLathe(this,new(9.5f,6.23f,8.5f),folds,canvas,"furled-canvas-folds");roll.Basis=horizontal;roll.Name="FurledCanvas";
        foreach(float offset in new[]{-2.25f,-.75f,.75f,2.25f})
        {
            var lash=ShipLathe(this,new(9.5f,6.23f,8.5f),[(offset-.033f,.305f),(offset+.033f,.305f)],rope,"canvas-lash:"+offset);lash.Basis=horizontal;
            Sphere(this,new(9.5f+offset,6.08f,8.78f),Vector3.One*.115f,rope);
            ShipRope(new(9.5f+offset,6.08f,8.77f),new(9.5f+offset+.06f,5.85f,8.82f),.023f,rope);
        }
        // Short folded ends make the bundle read as canvas bound to a spar.
        foreach(float sign in new[]{-1f,1f})
            Plate(this,new(9.5f+sign*2.66f,6.15f,8.48f),[new(-.12f,0),new(.17f,0),new(.23f,-.36f),new(-.04f,-.29f)],.025f,canvas);
    }

    private static readonly Vector2[] HullOutline=[new(0,-.5f),new(3,-2.8f),new(16,-2.8f),new(19,-.5f),
        new(19.45f,4),new(19.45f,16),new(18.8f,19.1f),new(15.5f,20.7f),new(9.5f,22),new(3.5f,20.7f),new(.2f,19.1f),new(-.45f,16),new(-.45f,4)];

    private static Vector3 HullPoint(int i,float t)
    {
        var p=HullOutline[i];float top=p.X<2||p.X>17?1.17f:p.Y<0?.65f:-.2f;
        float scale=1-t*.25f;
        return new(9.5f+(p.X-9.5f)*scale,Mathf.Lerp(top,-3.0f,t),9.5f+(p.Y-9.5f)*scale);
    }

    private static ArrayMesh ShipHullBand(float from,float to)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(c-a).Cross(b-a).Normalized();var middle=(a+b+c)/3;
            if(normal.Dot(middle-new Vector3(9.5f,-1.0f,9.5f))<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X*.17f+p.Z*.12f,p.Y));surface.AddVertex(p);}
        }
        for(int i=0;i<HullOutline.Length;i++)
        {
            int n=(i+1)%HullOutline.Length;var a=HullPoint(i,from);var b=HullPoint(n,from);var c=HullPoint(n,to);var d=HullPoint(i,to);
            Triangle(a,b,c);Triangle(a,c,d);
        }
        surface.Index();return surface.Commit();
    }

    private static ArrayMesh ShipBowDeck()
    {
        Vector2[] outline=[new(.2f,18.92f),new(18.8f,18.92f),new(18.8f,19.1f),new(15.5f,20.7f),new(9.5f,22),new(3.5f,20.7f),new(.2f,19.1f)];
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(int i=0;i<outline.Length;i++)
        {
            Vector3 a=new(9.5f,-.205f,20),b=new(outline[i].X,-.205f,outline[i].Y),c=new(outline[(i+1)%outline.Length].X,-.205f,outline[(i+1)%outline.Length].Y);
            if((c-a).Cross(b-a).Y<0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetNormal(Vector3.Up);surface.SetUV(new(p.X*.2f,p.Z));surface.AddVertex(p);}
        }
        surface.Index();return surface.Commit();
    }

    private static ArrayMesh ShipAnnulus(float inner,float outer,float depth)
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int i,float radius,float y){var(sin,cos)=PresentationMath.SinCos(i*Mathf.Tau/40);return new(cos*radius,y,sin*radius);}
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
        {
            foreach(var triangle in new[]{new[]{a,b,c},new[]{a,c,d}})
            {
                if((triangle[2]-triangle[0]).Cross(triangle[1]-triangle[0]).Dot(normal)<0)(triangle[1],triangle[2])=(triangle[2],triangle[1]);
                foreach(var p in triangle){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
            }
        }
        for(int i=0;i<40;i++)
        {
            int n=i+1;var a=Point(i,inner,depth/2);var b=Point(n,inner,depth/2);var c=Point(n,outer,depth/2);var d=Point(i,outer,depth/2);
            Quad(a,b,c,d,Vector3.Up);Quad(a-Vector3.Up*depth,b-Vector3.Up*depth,c-Vector3.Up*depth,d-Vector3.Up*depth,Vector3.Down);
            var normal=new Vector3(c.X+d.X,0,c.Z+d.Z).Normalized();
            Quad(d,c,c-Vector3.Up*depth,d-Vector3.Up*depth,normal);
            Quad(a,b,b-Vector3.Up*depth,a-Vector3.Up*depth,-normal);
        }
        surface.Index();return surface.Commit();
    }
}
