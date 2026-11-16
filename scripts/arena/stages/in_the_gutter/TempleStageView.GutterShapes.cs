using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D? _gutterDialNeedle;
    private float _gutterDialAngle;
    // Every owned triangle is emitted clockwise against its declared outward
    // normal. The shape journey audits the actual packed arrays after indexing.
    private static void GutterTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc)
    {
        if((b-a).Cross(c-a).Dot(na+nb+nc)>=0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
        foreach(var (p,n) in new[]{(a,na),(b,nb),(c,nc)})
        {
            surface.SetNormal(n);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);
        }
    }

    private static void GutterFlatTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
    {
        var normal=(c-a).Cross(b-a).Normalized();
        if(normal.Dot(outward)<0)normal=-normal;
        GutterTriangle(surface,a,b,c,normal,normal,normal);
    }

    private static MeshInstance3D GutterRing(Node parent,Vector3 position,float radius,float tube,Material material,Vector3? angles=null)
    {
        string key=$"gutter-ring:{radius}:{tube}";
        if(!SceneryMeshes.TryGetValue(key,out var mesh))
        {
            const int major=24,minor=8;
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            (Vector3 Point,Vector3 Normal) At(int i,int j)
            {
                var (sin,cos)=PresentationMath.SinCos(i*Mathf.Tau/major);
                var (st,ct)=PresentationMath.SinCos(j*Mathf.Tau/minor);
                var n=new Vector3(cos*ct,sin*ct,st);
                return (new Vector3(cos*radius,sin*radius,0)+n*tube,n);
            }
            for(int i=0;i<major;i++)for(int j=0;j<minor;j++)
            {
                var a=At(i,j);var b=At(i+1,j);var c=At(i+1,j+1);var d=At(i,j+1);
                GutterTriangle(surface,a.Point,b.Point,c.Point,a.Normal,b.Normal,c.Normal);
                GutterTriangle(surface,a.Point,c.Point,d.Point,a.Normal,c.Normal,d.Normal);
            }
            surface.Index();SceneryMeshes[key]=mesh=surface.Commit();
        }
        var node=new MeshInstance3D{Name="CopperLockRing",Position=position,Mesh=mesh,MaterialOverride=material,
            Basis=PresentationMath.Rotation(angles??Vector3.Zero)};
        parent.AddChild(node);return node;
    }

    private static ArrayMesh GutterGearDisc()
    {
        const string key="copper-lock-solid-gear";
        if(SceneryMeshes.TryGetValue(key,out var cached))return (ArrayMesh)cached;
        const float radius=.94f,hole=.2f,center=.34f,halfDepth=.22f;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var outer=new List<Vector2>();
        static float Angle(float y,float x)
        {
            float a=x==0?(y>=0?Mathf.Pi/2:-Mathf.Pi/2):PresentationMath.Atan(y/x)+(x<0?Mathf.Pi:0);
            return a<0?a+Mathf.Tau:a;
        }
        var angles=Enumerable.Range(0,32).Select(i=>i*Mathf.Tau/32).ToList();
        angles.AddRange([Angle(-center,-center),Angle(-center,radius-center),Angle(radius-center,-center)]);
        angles=angles.Order().DistinctBy(a=>MathF.Round(a*100000)).ToList();
        for(int quadrant=0;quadrant<4;quadrant++)
        {
            var (sin,cos)=PresentationMath.SinCos(quadrant*Mathf.Pi/2);
            Vector2 Rotate(Vector2 p)=>new(p.X*cos-p.Y*sin,p.X*sin+p.Y*cos);
            var inner=new List<Vector2>();var boundary=new List<Vector2>();
            foreach(float angle in angles)
            {
                var (ss,cc)=PresentationMath.SinCos(angle);var d=new Vector2(cc,ss);var c=new Vector2(center,center);
                float dot=c.Dot(d);
                float t=-dot+MathF.Sqrt(dot*dot+radius*radius-c.LengthSquared());
                bool circle=true;
                if(d.X<-.00001f&&-center/d.X<t){t=-center/d.X;circle=false;}
                if(d.Y<-.00001f&&-center/d.Y<t){t=-center/d.Y;circle=false;}
                inner.Add(Rotate(c+d*hole));
                var edge=Rotate(c+d*t);boundary.Add(edge);
                if(circle)outer.Add(edge);
            }
            for(int i=0;i<angles.Count;i++)
            {
                int j=(i+1)%angles.Count;
                foreach(float z in new[]{-halfDepth,halfDepth})
                {
                    var a=new Vector3(inner[i].X,inner[i].Y,z);var b=new Vector3(boundary[i].X,boundary[i].Y,z);
                    var c=new Vector3(boundary[j].X,boundary[j].Y,z);var d=new Vector3(inner[j].X,inner[j].Y,z);
                    var n=z>0?Vector3.Back:Vector3.Forward;
                    GutterFlatTriangle(surface,a,b,c,n);GutterFlatTriangle(surface,a,c,d,n);
                }
                var ia=new Vector3(inner[i].X,inner[i].Y,-halfDepth);var ib=new Vector3(inner[j].X,inner[j].Y,-halfDepth);
                var ic=new Vector3(inner[j].X,inner[j].Y,halfDepth);var id=new Vector3(inner[i].X,inner[i].Y,halfDepth);
                var c2=Rotate(new(center,center));var normal=new Vector3(c2.X,c2.Y,0)-(ia+ib)*.5f;normal.Z=0;
                GutterFlatTriangle(surface,ia,ib,ic,normal);GutterFlatTriangle(surface,ia,ic,id,normal);
            }
        }
        // Exact quadrant endpoints close the circular outside rim.
        outer.AddRange([new(radius,0),new(0,radius),new(-radius,0),new(0,-radius)]);
        outer=outer.OrderBy(p=>Angle(p.Y,p.X)).DistinctBy(p=>(MathF.Round(p.X*100000),MathF.Round(p.Y*100000))).ToList();
        for(int i=0;i<outer.Count;i++)
        {
            int j=(i+1)%outer.Count;
            var a=new Vector3(outer[i].X,outer[i].Y,-halfDepth);var b=new Vector3(outer[j].X,outer[j].Y,-halfDepth);
            var c=new Vector3(outer[j].X,outer[j].Y,halfDepth);var d=new Vector3(outer[i].X,outer[i].Y,halfDepth);
            var n=(a+b)*.5f;n.Z=0;
            GutterFlatTriangle(surface,a,b,c,n);GutterFlatTriangle(surface,a,c,d,n);
        }
        surface.Index();var mesh=surface.Commit();SceneryMeshes[key]=mesh;return mesh;
    }

    private Node3D GutterGear(Node3D parent,Vector3 position,float phase,Material verdigris,Material brass)
    {
        var gear=Prop(parent,"CopperLockDriveGear",position);
        gear.AddChild(new MeshInstance3D{Name="CopperLockSolidGearDisc",Mesh=GutterGearDisc(),MaterialOverride=brass});
        for(int tooth=0;tooth<10;tooth++)
        {
            float angle=tooth*Mathf.Tau/10;var (sin,cos)=PresentationMath.SinCos(angle);
            var tip=Box(gear,new(cos*1.07f,sin*1.07f,0),new(.37f,.32f,.46f),brass,.07f);
            tip.Basis=PresentationMath.Rotation(new(0,0,angle));
        }
        RoundedBox(gear,Vector3.Zero,new(.43f,.43f,.57f),brass,.8f);
        Sphere(gear,new(0,0,.36f),new(.66f,.66f,.25f),verdigris).Name="CopperLockRoundAxleCap";
        _gutterGears.Add((gear,phase));
        return gear;
    }

    private void GutterGantry(Node3D parent,int side,Material verdigris,Material brass,Material cream)
    {
        float x=side<0?.5f:Width-.5f;
        var machine=Prop(parent,side<0?"CopperLockWestMachine":"CopperLockEastMachine",new(x,0,0));
        // Six large cast bars are the gate itself, rather than a thin fence.
        var gate=Prop(machine,"CopperLockGatePanel",Vector3.Zero);
        _gutterGates.Add(gate);
        Box(gate,new(0,.16f,Depth/2f),new(.55f,.32f,Depth-1.6f),verdigris,.11f);
        for(int bar=0;bar<6;bar++)
        {
            float z=1.8f+bar*(Depth-3.6f)/5;
            Box(gate,new(0,1.39f,z),new(.44f,2.22f,.44f),verdigris,.15f).Name="CopperLockFatGateBar";
            Sphere(gate,new(0,2.61f,z),Vector3.One*.58f,brass).Name="CopperLockGateFinial";
        }
        Box(gate,new(0,2.25f,Depth/2f),new(.55f,.34f,Depth-1.6f),verdigris,.11f);
        // A bowed, thick beam makes an independently readable cast gantry.
        var beam=new SurfaceTool();beam.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=12;const float half=.48f;
        Vector3 Point(int step,bool top,bool outside)
        {
            float t=step/(float)segments,z=.65f+t*(Depth-1.3f);
            float y=2.98f+PresentationMath.Sin(t*Mathf.Pi)*.38f+(top?.54f:-.54f);
            return new(outside?half:-half,y,z);
        }
        for(int i=0;i<segments;i++)
        {
            var a=Point(i,false,false);var b=Point(i+1,false,false);var c=Point(i+1,true,false);var d=Point(i,true,false);
            GutterFlatTriangle(beam,a,b,c,Vector3.Left);GutterFlatTriangle(beam,a,c,d,Vector3.Left);
            a=Point(i,false,true);b=Point(i+1,false,true);c=Point(i+1,true,true);d=Point(i,true,true);
            GutterFlatTriangle(beam,a,b,c,Vector3.Right);GutterFlatTriangle(beam,a,c,d,Vector3.Right);
            foreach(bool top in new[]{false,true})
            {
                a=Point(i,top,false);b=Point(i+1,top,false);c=Point(i+1,top,true);d=Point(i,top,true);
                GutterFlatTriangle(beam,a,b,c,top?Vector3.Up:Vector3.Down);GutterFlatTriangle(beam,a,c,d,top?Vector3.Up:Vector3.Down);
            }
        }
        foreach(int i in new[]{0,segments})
        {
            var a=Point(i,false,false);var b=Point(i,false,true);var c=Point(i,true,true);var d=Point(i,true,false);
            var normal=i==0?Vector3.Forward:Vector3.Back;
            GutterFlatTriangle(beam,a,b,c,normal);GutterFlatTriangle(beam,a,c,d,normal);
        }
        beam.Index();machine.AddChild(new MeshInstance3D{Name="CopperLockBowedGantry",Mesh=beam.Commit(),MaterialOverride=verdigris});
        foreach(float z in new[]{.95f,Depth-.95f})
        {
            float tower=z<Depth/2f?5.2f:3.05f;
            float outboard=side*.26f;
            RoundedBox(machine,new(outboard,tower/2,z),new(1.41f,tower,1.67f),cream,.24f).Name="CopperLockGantryTower";
            RoundedBox(machine,new(outboard,.2f,z),new(1.48f,.4f,1.84f),verdigris,.4f);
            Sphere(machine,new(outboard,tower+.15f,z),new(1.42f,.59f,1.66f),brass);
        }
        // Offset mechanisms form an asymmetric pair in the side margins, below HUD.
        float gearZ=Depth*.5f,outerX=side*4f;
        GutterGear(machine,new(outerX,2.05f,gearZ),side<0?.14f:.7f,verdigris,brass);
        Box(machine,new(outerX,2.05f,gearZ-.31f),new(2.1f,.4f,.55f),verdigris,.11f);
        Box(machine,new(side*2f,2.05f,gearZ),new(4f,.38f,.38f),brass,.1f);
        float counterZ=gearZ-2.6f; // Both weights hang behind the gear faces.
        RoundedBox(machine,new(outerX,2.63f,gearZ),new(.3f,1.2f,.3f),verdigris,.5f);
        Box(machine,new(outerX,3.19f,(gearZ+counterZ)/2),new(.3f,.3f,Math.Abs(counterZ-gearZ)+.3f),verdigris,.09f);
        Box(machine,new(outerX,2.5f,counterZ),new(.2f,1.4f,.2f),brass,.07f);
        RoundedBox(machine,new(outerX,1.48f,counterZ),new(.97f,1.28f,.84f),verdigris,.45f).Name="CopperLockCounterweight";
        Box(machine,new(outerX,1.5f,counterZ+.39f),new(.68f,.22f,.08f),brass,.025f);
        if(side<0)
        {
            // One active machine carries the large porthole gauge. The other
            // gantry remains quieter; no added hut or repeat house vignette.
            RoundedBox(machine,new(outerX,2.85f,counterZ),new(.5f,.65f,.45f),verdigris,.45f);
            var dial=Prop(machine,"CopperLockPressureDial",new(outerX,3.5f,counterZ));
            dial.Basis=PresentationMath.Rotation(new(-.60f,.38f,0));
            Sphere(dial,Vector3.Zero,new(1.5f,1.5f,.25f),cream);
            GutterRing(dial,new(0,0,.12f),.63f,.09f,brass);
            Sphere(dial,new(0,0,.14f),new(1.08f,1.08f,.09f),verdigris);
            _gutterDialNeedle=Prop(dial,"CopperLockCurrentNeedle",new(0,0,.23f));
            RoundedBox(_gutterDialNeedle,new(.16f,0,0),new(.53f,.10f,.04f),brass,.6f);
            Sphere(dial,new(0,0,.25f),Vector3.One*.13f,brass);
        }
    }

    private void GutterHalfRoundCoping(float z,float top,Material cream)
    {
        const int sides=12;const float radius=.25f;
        float length=Width-2;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 Point,Vector3 Normal) At(float x,int step)
        {
            var (sin,cos)=PresentationMath.SinCos(step*Mathf.Pi/sides);
            return (new(x,sin*radius,cos*radius),new(0,sin,cos));
        }
        for(int i=0;i<sides;i++)
        {
            var a=At(0,i);var b=At(length,i);var c=At(length,i+1);var d=At(0,i+1);
            GutterTriangle(surface,a.Point,b.Point,c.Point,a.Normal,b.Normal,c.Normal);
            GutterTriangle(surface,a.Point,c.Point,d.Point,a.Normal,c.Normal,d.Normal);
            GutterFlatTriangle(surface,Vector3.Zero,a.Point,d.Point,Vector3.Left);
            GutterFlatTriangle(surface,new(length,0,0),b.Point,c.Point,Vector3.Right);
        }
        var p0=new Vector3(0,0,-radius);var p1=new Vector3(length,0,-radius);
        var p2=new Vector3(length,0,radius);var p3=new Vector3(0,0,radius);
        GutterFlatTriangle(surface,p0,p1,p2,Vector3.Down);GutterFlatTriangle(surface,p0,p2,p3,Vector3.Down);
        surface.Index();AddChild(new MeshInstance3D{Name="CopperLockHalfRoundCoping",Position=new(1,top-radius,z),
            Mesh=surface.Commit(),MaterialOverride=cream});
    }

    private void GutterBollard(int x,int z,Material brick,Material cream,Material brass,Material algae)
    {
        float top=2*HeightScale;
        var post=Prop(this,"CopperLockBollard",new(x+1,0,z+1));
        RoundedBox(post,new(0,(top-.32f)/2,0),new(1.92f,top-.32f,1.84f),brick,.28f).Name="GutterRaisedPost";
        RoundedBox(post,new(0,.16f,0),new(1.99f,.32f,1.99f),algae,.35f);
        RoundedBox(post,new(0,top-.2f,0),new(1.99f,.4f,1.99f),cream,.38f).Name="CopperLockBollardCap";
        // The ring is recessed within the original two-cell footprint.
        GutterRing(post,new(0,top*.62f,.93f),.28f,.09f,brass);
        RoundedBox(post,new(0,top*.62f,.80f),new(.54f,.56f,.15f),brick,.25f);
        AddGutterWake(post,new(0,.07f,0),1.95f);
        AddGutterFoam(post,new(0,.074f,0),2.15f);
    }

    private void GutterFishVane(Material brick,Material cream,Material brass,Material dark)
    {
        float x=10.5f,z=10.5f,top=1.1f*HeightScale;
        var post=Prop(this,"CopperLockCenterBollard",new(x,0,z));
        RoundedBox(post,new(0,.28f,0),new(.98f,.56f,.98f),brick,.35f);
        const float poleTop=2.55f;
        for(int band=0;band<7;band++)
            RoundedBox(post,new(0,.62f+band*.29f,0),new(.25f,.3f,.25f),band%2==0?cream:brass,.75f);
        RoundedBox(post,new(0,top-.07f,0),new(.83f,.14f,.83f),cream,.5f);
        _gutterArrow=Prop(post,"GutterCurrentMarker",new(0,poleTop+.25f,0));
        Sphere(_gutterArrow,new(.06f,0,0),new(.92f,.48f,.25f),brass).Name="CopperLockFishBody";
        Plate(_gutterArrow,new(-.47f,0,0),[new(-.19f,-.23f),new(.15f,0),new(-.19f,.23f)],.17f,brass);
        Plate(_gutterArrow,new(.02f,.21f,0),[new(-.2f,0),new(.17f,0),new(-.08f,.22f)],.12f,brass);
        foreach(int face in new[]{-1,1})
            Sphere(_gutterArrow,new(.35f,.07f,face*.126f),Vector3.One*.085f,dark);
        AddGutterWake(post,new(0,.07f,0),1);
        AddGutterFoam(post,new(0,.074f,0),1.1f);
    }

    private void GutterWorkLantern(Vector3 position,Material verdigris,Material brass)
    {
        var lamp=Prop(this,position.X<Width/2f?"CopperLockWestLantern":"CopperLockEastLantern",position);
        RoundedBox(lamp,Vector3.Zero,new(.56f,.75f,.48f),verdigris,.4f);
        Box(lamp,new(0,.01f,.25f),new(.46f,.59f,.035f),Glow(new Color("ffc078"),1.55f),.025f);
        RoundedBox(lamp,new(0,.43f,0),new(.69f,.14f,.61f),brass,.6f);
        var light=new OmniLight3D{Name="CopperLockWorkLight",Position=new(0,0,.4f),
            LightColor=new Color("eac084"),LightEnergy=.7f,OmniRange=4.5f,ShadowEnabled=false,
            Visible=GraphicsQuality.Current!=GraphicsPreset.Low};
        lamp.AddChild(light);_gutterWorkLights.Add(light);
    }

    public void SetGutterCompression(float left,float right)
    {
        if(!_gutter||_gutterGates.Count!=2)return;
        _gutterGates[0].Position=new(Math.Max(0,left-.72f),0,0);
        _gutterGates[1].Position=new(-Math.Max(0,right-.72f),0,0);
    }
}
