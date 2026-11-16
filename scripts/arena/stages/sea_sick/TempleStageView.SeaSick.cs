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
        var oak=ShipPaint("b98450",sailShadow:true);var pale=ShipPaint("c18e59",sailShadow:true);var dark=ShipPaint("795235");
        _barrelTimber=ShipPaint("a66f3f");
        var green=_art.Toy(new("356f63"),.65f);var gold=_art.Color(new("c7a15b"),.65f);
        var iron=_art.Color(new("424d53"),.85f);
        BuildShipHull(oak);
        BuildShipPlanks(oak,pale);
        BuildShipFinish(oak,dark,gold);

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
            // The deck is a thin slab. Only the gallery's inner riser is tall;
            // the outer hull is shaped separately rather than a solid box tray.
            if(x is not (0 or 18))
                Box(this,new(x+.5f,(y-.14f-.38f)/2,z==0?.775f:z==18?18.225f:z+.5f),new(.998f,y-.14f+.38f,z is 0 or 18?.45f:.998f),dark);
            if(x is 0 or 18||z is 0 or 18)
            {
                bool side=x is 0 or 18;
                Box(this,new(x+.5f,y+.20f,z+.5f),new(side?.22f:.99f,.40f,side?.99f:.22f),green,.025f).Name="Gunwale";
                Box(this,new(x+.5f,y+.41f,z+.5f),new(side?.28f:.99f,.07f,side?.99f:.28f),gold,.018f);
                Sphere(this,new(x+.5f,y+.58f,z+.5f),Vector3.One*.13f,gold);
                ShipRope(new(x+(side?.5f:0),y+.55f,z+(side?0:.5f)),new(x+(side?.5f:1),y+.55f,z+(side?1:.5f)),.055f,gold);
            }
        }
        foreach(var (x,z) in new[]{(9,8),(5,11),(13,11)})
        {
            float height=Layout.At(x,z).ObstacleHeight!.Value*HeightScale;
            var post=new Node3D{Name=x==9?"MainMast":"ForwardPost"+x,Position=new(x+.5f,0,z+.5f)};AddChild(post);
            if(x==9)
            {
                var paint=ShipPaint("ae7a49",true);
                ShipLathe(post,Vector3.Zero,[(0,.46f),(1.15f,.44f),(height-.25f,.34f),(height,.30f)],paint,"main-mast");
                // Upper spar is cosmetic and exits the battle frame cleanly.
                if(IncludeScenery)
                {
                    var upper=ShipLathe(post,Vector3.Zero,[(height,.30f),(12.4f,.18f)],paint,"upper-mast");
                    upper.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
                }
            }
            else if(x==5)
            {
                ShipLathe(post,Vector3.Zero,[(0,.45f),(.22f,.47f),(.38f,.34f),(height-.20f,.34f),(height-.12f,.47f),(height,.47f)],oak,"capstan-post");
            }
            else
            {
                foreach(float dx in new[]{-.25f,.25f})Box(post,new(dx,height/2,0),new(.31f,height,.66f),oak,.07f);
                Box(post,new(0,height-.23f,0),new(.93f,.22f,.72f),oak,.07f);
            }
            Box(post,new(0,.10f,0),new(.95f,.20f,.95f),pale,.025f);
            foreach(float band in new[]{.25f,Mathf.Min(height-.12f,1.35f)})
                if(x==9)ShipLathe(post,Vector3.Zero,[(band-.045f,.47f),(band+.045f,.47f)],iron,"mast-hoop:"+band);
                else Box(post,new(0,band,0),new(.82f,.075f,.82f),iron,.014f);

        }
        // Thick shrouds finish beyond the court; their in-court spans remain
        // above standing headroom. Sail/decor refinement follows shape approval.
        foreach(float x in new[]{8.1f,10.9f})
        {
            var eye=ShipLathe(this,new(x,.445f,.35f),[(0,.105f),(1.2f,.105f)],gold,"far-deadeye");
            eye.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            ShipRope(new(9.5f,IncludeScenery?12:6.9f,8.5f),new(x,1.65f,.35f),.075f,gold).Name=$"FarShroud{x}";
        }
        ShipRope(new(9.5f,IncludeScenery?12:6.9f,8.5f),TubbyBowspritTip(),.075f,gold).Name="Forestay";
        ShipRope(new(9.5f,IncludeScenery?12:6.9f,8.5f),new(-1.3f,3.2f,9.5f),.075f,gold).Name="Backstay";

        if(IncludeScenery)
        {
            // Fixed world sea; the complete ship, actors and effects rock together
            // above it in ArenaView's StageAssembly, leaving the horizon steady.
            _water=new MeshInstance3D{Name="ShipOcean",TopLevel=true,Mesh=PrimitiveTemplates.Get("PlaneMesh",("size",new Vector2(160,160)),("subdivide_width",180),("subdivide_depth",180)),
                MaterialOverride=new ShaderMaterial{Shader=ArtMaterials.SeaWaterShaderFor(GraphicsQuality.Current),RenderPriority=-16},Position=new(Width/2f,-2.1f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_water);
            AddChild(new MeshInstance3D{Name="Seabed",TopLevel=true,Mesh=PrimitiveTemplates.Get("BoxMesh",("size",new Vector3(160,.05f,160))),
                MaterialOverride=_art.Color(new("0f3a46"),.95f),Position=new(Width/2f,-4.5f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
    }

    /// <summary>Opaque display sea occludes only the submerged hull, retaining
    /// the belly down to its real waterline while seating it on the menu stand.</summary>
    public void FitSeaSickMiniature(float radius)
    {
        if(!_seaSick||IncludeScenery)return;
        var disc=ShipLathe(this,new(12.2f,-2.1f,9.5f),[(-.06f,radius),(0,radius)],_art.Color(new("24586b"),.72f),"miniature-sea:"+radius);
        disc.Name="SeaSickMiniatureSea";disc.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private void BuildShipPlanks(Material oak,Material pale)
    {
        var pegs=new List<Transform3D>();
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;)
        {
            var cell=Layout.At(x,z);
            bool InnerPost(int at)=>at>0&&at<Width-1&&z>0&&z<Depth-1&&Layout.At(at,z).Kind==CellKind.Pillar;
            if(cell.Kind==CellKind.Ramp||InnerPost(x)){x++;continue;}
            int run=1,limit=3+(x+z*7)%4;
            while(run<limit&&x+run<Width&&Layout.At(x+run,z).Tier==cell.Tier&&Layout.At(x+run,z).Kind!=CellKind.Ramp&&!InnerPost(x+run))run++;
            Box(this,new(x+run/2f,cell.Tier*HeightScale-.07f,z==0?.70f:z==18?18.30f:z+.5f),new(run-.015f,.14f,z is 0 or 18?.60f:.985f),
                (x*7+z*13)%4==0?pale:oak,.014f).Name=$"DeckPlank{x}_{z}";
            float zz=z==0?.70f:z==18?18.30f:z+.5f;
            foreach(float xx in new[]{x+.19f,x+run-.19f})foreach(float dz in new[]{-.25f,.25f})
                pegs.Add(new Transform3D(Basis.Identity.Scaled(new(.18f,.025f,.18f)),new(xx,cell.Tier*HeightScale+.006f,zz+dz)));
            x+=run;
        }
        var dots=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,
            Mesh=PrimitiveTemplates.Get("SphereMesh",("radius",.5f),("height",1),("radial_segments",10),("rings",5)),InstanceCount=pegs.Count};
        var buffer=new float[pegs.Count*MultiMeshPacking.Stride];
        for(int i=0;i<pegs.Count;i++)MultiMeshPacking.Write(buffer,i,pegs[i],Colors.White);
        dots.Buffer=buffer;
        AddChild(new MultiMeshInstance3D{Name="DeckPegs",Multimesh=dots,MaterialOverride=_art.Color(new("6a4b35"),.9f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private ShaderMaterial ShipPaint(string hex,bool vertical=false,int style=0,bool sailShadow=false)
    {
        var paint=_art.ShipTimber(new(hex),vertical,style,sailShadow);_shipMaterials.Add(paint);return paint;
    }

    private void ProcessSeaSick(float delta)
    {
        if(WeatherActive){_shipLifeClock+=delta;foreach(var motion in _shipLife)motion(_shipLifeClock);}
        var inverse=GlobalTransform.AffineInverse();float roll=-GlobalBasis.Z.Y;
        foreach(var paint in _shipMaterials){paint.SetShaderParameter("ship_inverse",inverse);paint.SetShaderParameter("ship_roll",roll);}
        if(_water?.MaterialOverride is ShaderMaterial sea)
        {sea.SetShaderParameter("ship_inverse",inverse);sea.SetShaderParameter("ambient_clock",_shipLifeClock);}
        if(_shipLanternLight!=null)_shipLanternLight.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
        if(_graphicsRevision==GraphicsQuality.Revision)return;
        foreach(var paint in _shipMaterials)GraphicsQuality.ApplyShaderDetail(paint);
        if(_water?.MaterialOverride is ShaderMaterial ocean)
        {
            ocean.Shader=ArtMaterials.SeaWaterShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(ocean);if(_lighting is {} look){look.ApplyWater(ocean);ApplyShipLighting(look);}
            ocean.SetShaderParameter("seabed_height",-4.5f);
        }
        _graphicsRevision=GraphicsQuality.Revision;
    }

    private Node3D CreateShipBarrel(Node3D root)
    {
        root.Name=$"ShipBarrel{(int)root.Position.X}_{(int)root.Position.Z}";root.SetMeta("sea_sick_barrel",true);
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

    private MeshInstance3D ShipRope(Vector3 a,Vector3 b,float radius,Material paint)
    {
        var delta=b-a;float length=delta.Length();
        var rope=ShipLathe(this,(a+b)/2,[(-length/2,radius),(length/2,radius)],paint,$"rope:{length}:{radius}");
        var up=delta/length;var right=up.Cross(Math.Abs(up.Z)<.9f?Vector3.Back:Vector3.Right).Normalized();
        rope.Basis=new(right,up,right.Cross(up));rope.Name="Rigging";
        rope.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;return rope;
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

}
