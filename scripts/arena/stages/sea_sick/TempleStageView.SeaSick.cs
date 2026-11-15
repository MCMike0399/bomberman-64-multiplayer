using Godot;
using BlastCircuit.Rendering;
using Bomber.Game.Simulation.Stages;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private static readonly Dictionary<string,ArrayMesh> ShipMeshes=[];

    private void BuildSeaSick()
    {
        Name="SeaSickTerrain";
        var oak=ShipPaint("b98450");var pale=ShipPaint("c99961");var dark=ShipPaint("795235");
        _barrelTimber=ShipPaint("a66f3f");
        var green=_art.Toy(new("356f63"),.65f);var gold=_art.Color(new("c7a15b"),.65f);
        var iron=_art.Color(new("424d53"),.85f);
        var hull=new MeshInstance3D{Name="CutterHull",Mesh=ShipHullMesh(),MaterialOverride=dark};AddChild(hull);
        foreach(float side in new[]{-.32f,19.32f})
        {
            Box(this,new(side,-.50f,9.5f),new(.09f,.28f,16.5f),green,.03f);
            Box(this,new(side,-.70f,9.5f),new(.10f,.045f,16.5f),gold,.012f);
        }
        BuildShipPlanks(oak,pale);

        // The collision grid owns the real decks and ramps. Individual broad
        // planks are quiet geometry with recessed joints, never a printed grid.
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);float y=cell.Tier*HeightScale;
            if(cell.Kind==CellKind.Ramp)
            {
                float rise=(cell.RampToTier-cell.RampFromTier)*HeightScale;
                for(int step=0;step<6;step++)
                {
                    float run=(step+.5f)/6-.5f,height=rise*(step+1)/6;
                    Box(this,new(x+.5f+cell.RampDX*run,y+height/2-.018f,z+.5f),new(1f/6-.006f,height-.036f,.98f),oak);
                    Box(this,new(x+.5f+cell.RampDX*run,y+height-.018f,z+.5f),new(1f/6-.006f,.036f,.98f),pale,.004f).Name="GalleryTread";
                }
                continue;
            }
            if(cell.Kind==CellKind.Pillar&&x>0&&x<Width-1&&z>0&&z<Depth-1)continue;
            Box(this,new(x+.5f,(y-.14f-1.15f)/2,z+.5f),new(.998f,y-.14f+1.15f,.998f),dark);
            if(x is 0 or 18||z is 0 or 18)
            {
                bool side=x is 0 or 18;
                Box(this,new(x+.5f,y+.20f,z+.5f),new(side?.22f:.99f,.40f,side?.99f:.22f),green,.025f).Name="Gunwale";
                Box(this,new(x+.5f,y+.41f,z+.5f),new(side?.28f:.99f,.07f,side?.99f:.28f),gold,.018f);
                Sphere(this,new(x+.5f,y+.58f,z+.5f),Vector3.One*.13f,gold);
                ShipRope(new(x+(side?.5f:0),y+.55f,z+(side?0:.5f)),new(x+(side?.5f:1),y+.55f,z+(side?1:.5f)),.026f,gold);
            }
        }
        foreach(var (x,z) in new[]{(9,8),(5,11),(13,11)})
        {
            float height=Layout.At(x,z).ObstacleHeight!.Value*HeightScale;
            var post=new Node3D{Name=x==9?"MainMast":"ForwardPost"+x,Position=new(x+.5f,0,z+.5f)};AddChild(post);
            Box(post,new(0,height/2,0),new(.72f,height,.72f),oak,.045f);
            Box(post,new(0,.10f,0),new(.95f,.20f,.95f),pale,.025f);
            foreach(float band in new[]{.25f,Mathf.Min(height-.12f,1.35f)})
                Box(post,new(0,band,0),new(.77f,.075f,.77f),iron,.012f);
            if(x!=9)Box(post,new(0,height-.055f,0),new(.81f,.11f,.81f),gold,.025f);
        }
        // A furled sail and high rigging frame the deck without a canvas sheet
        // covering the playable lanes. Attachment ends remain on the gunwales.
        Box(this,new(9.5f,6.55f,8.5f),new(6.2f,.17f,.17f),dark,.04f).Name="MastYard";
        Box(this,new(9.5f,6.31f,8.5f),new(5.7f,.35f,.40f),_art.Color(new("e5dcc4"),.93f),.12f).Name="FurledCanvas";
        foreach(float x in new[]{7.3f,8.7f,10.3f,11.7f})Box(this,new(x,6.31f,8.5f),new(.075f,.37f,.42f),gold,.02f);
        foreach(float x in new[]{.5f,18.5f})
            ShipRope(new(9.5f,6.9f,8.5f),new(x,2.1f,1.5f),.032f,gold);

        if(IncludeScenery)
        {
            // Fixed world sea; the complete ship, actors and effects rock together
            // above it in ArenaView's StageAssembly, leaving the horizon steady.
            _water=new MeshInstance3D{Name="ShipOcean",TopLevel=true,Mesh=PrimitiveTemplates.Get("PlaneMesh",("size",new Vector2(160,160)),("subdivide_width",180),("subdivide_depth",180)),
                MaterialOverride=_art.Water(),Position=new(Width/2f,-2.1f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_water);
            AddChild(new MeshInstance3D{Name="Seabed",TopLevel=true,Mesh=PrimitiveTemplates.Get("BoxMesh",("size",new Vector3(160,.05f,160))),
                MaterialOverride=_art.Color(new("0f3a46"),.95f),Position=new(Width/2f,-4.5f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
    }

    private void BuildShipPlanks(Material oak,Material pale)
    {
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;)
        {
            var cell=Layout.At(x,z);
            bool InnerPost(int at)=>at>0&&at<Width-1&&z>0&&z<Depth-1&&Layout.At(at,z).Kind==CellKind.Pillar;
            if(cell.Kind==CellKind.Ramp||InnerPost(x)){x++;continue;}
            int run=1,limit=2+(x+z*3)%3;
            while(run<limit&&x+run<Width&&Layout.At(x+run,z).Tier==cell.Tier&&Layout.At(x+run,z).Kind!=CellKind.Ramp&&!InnerPost(x+run))run++;
            Box(this,new(x+run/2f,cell.Tier*HeightScale-.07f,z+.5f),new(run-.015f,.14f,.985f),
                (x*7+z*13)%4==0?pale:oak,.014f).Name=$"DeckPlank{x}_{z}";
            x+=run;
        }
    }

    private ShaderMaterial ShipPaint(string hex)
    {
        var paint=_art.ShipTimber(new(hex));_shipMaterials.Add(paint);return paint;
    }

    private void ProcessSeaSick()
    {
        if(_graphicsRevision==GraphicsQuality.Revision)return;
        foreach(var paint in _shipMaterials)GraphicsQuality.ApplyShaderDetail(paint);
        if(_water?.MaterialOverride is ShaderMaterial ocean)
        {
            ocean.Shader=ArtMaterials.WaterShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(ocean);_lighting?.ApplyWater(ocean);
            ocean.SetShaderParameter("seabed_height",-4.5f);
        }
        _graphicsRevision=GraphicsQuality.Revision;
    }

    private Node3D CreateShipBarrel(Node3D root)
    {
        root.Name=$"ShipBarrel{(int)root.Position.X}_{(int)root.Position.Z}";
        float height=Layout.SoftBlockHeight*HeightScale;
        ShipLathe(root,Vector3.Zero,[(0,.32f),(.12f,.37f),(height*.5f,.45f),(height-.12f,.37f),(height,.32f)],_barrelTimber,"barrel");
        var hoops=_art.Color(new("495a59"),.74f);
        foreach(float y in new[]{.17f,height-.17f})
            ShipLathe(root,Vector3.Zero,[(y-.055f,.405f),(y+.055f,.405f)],hoops,"barrel-hoop:"+y);
        // Broad lid and inset bung keep the barrel legible from the battle camera.
        ShipLathe(root,new(0,height+.006f,0),[(0,.31f),(.018f,.31f)],_barrelTimber,"barrel-lid");
        Box(root,new(.11f,height+.031f,.04f),new(.10f,.018f,.10f),hoops,.015f);
        return root;
    }

    private void ShipRope(Vector3 a,Vector3 b,float radius,Material paint)
    {
        var delta=b-a;float length=delta.Length();
        var rope=ShipLathe(this,(a+b)/2,[(-length/2,radius),(length/2,radius)],paint,$"rope:{length}:{radius}");
        var up=delta/length;var right=up.Cross(Math.Abs(up.Z)<.9f?Vector3.Back:Vector3.Right).Normalized();
        rope.Basis=new(right,up,right.Cross(up));rope.Name="Rigging";
    }

    /// <summary>Owned portable mesh generator: no new platform-native primitive tuples.</summary>
    private static MeshInstance3D ShipLathe(Node parent,Vector3 at,(float Y,float Radius)[] profile,Material paint,string key)
    {
        if(!ShipMeshes.TryGetValue(key,out var mesh))
        {
            const int sides=20;var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            Vector3 Point(int ring,int side)
            {
                var (sin,cos)=PresentationMath.SinCos(side*Mathf.Tau/sides);
                return new(cos*profile[ring].Radius,profile[ring].Y,sin*profile[ring].Radius);
            }
            void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
            {
                var normal=(c-a).Cross(b-a).Normalized();
                if(normal.Dot(outward)<0){(b,c)=(c,b);normal=-normal;}
                foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X+p.Z,p.Y));surface.AddVertex(p);}
            }
            for(int i=0;i<sides;i++)
            {
                for(int ring=0;ring<profile.Length-1;ring++)
                {
                    var a=Point(ring,i);var b=Point(ring,i+1);var c=Point(ring+1,i+1);var d=Point(ring+1,i);
                    var outward=new Vector3((a.X+b.X)/2,0,(a.Z+b.Z)/2);
                    Triangle(a,b,c,outward);Triangle(a,c,d,outward);
                }
                Triangle(new(0,profile[0].Y,0),Point(0,i+1),Point(0,i),Vector3.Down);
                int last=profile.Length-1;
                Triangle(new(0,profile[last].Y,0),Point(last,i),Point(last,i+1),Vector3.Up);
            }
            surface.Index();ShipMeshes[key]=mesh=surface.Commit();
        }
        var node=new MeshInstance3D{Mesh=mesh,MaterialOverride=paint,Position=at};parent.AddChild(node);return node;
    }

    private static ArrayMesh ShipHullMesh()
    {
        var top=new Vector3[]{new(-.3f,-.2f,1),new(1,-.2f,-.7f),new(18,-.2f,-.7f),new(19.3f,-.2f,1),new(19.3f,-.2f,17.8f),new(17,-.2f,20),new(2,-.2f,20),new(-.3f,-.2f,17.8f)};
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var center=new Vector3(9.5f,-1.25f,9.5f);
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(c-a).Cross(b-a).Normalized();var mid=(a+b+c)/3;
            if(normal.Dot(mid-center)<0){(b,c)=(c,b);normal=-normal;}
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X*.3f,p.Y*.4f));surface.AddVertex(p);}
        }
        for(int i=0;i<top.Length;i++)
        {
            int next=(i+1)%top.Length;var a=top[i];var b=top[next];
            var c=new Vector3(9.5f+(b.X-9.5f)*.70f,-2.5f,9.5f+(b.Z-9.5f)*.80f);
            var d=new Vector3(9.5f+(a.X-9.5f)*.70f,-2.5f,9.5f+(a.Z-9.5f)*.80f);
            Triangle(a,b,c);Triangle(a,c,d);Triangle(new(9.5f,-2.5f,9.5f),c,d);
        }
        surface.Index();return surface.Commit();
    }
}
