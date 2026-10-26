using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>A White Glacier-informed snowfield: frosted pine groves, an igloo,
    /// a snowman, penguins by a frozen pond, ice crystals, drifts and far peaks.
    /// The arena's walls, ice and wind remain the only gameplay surfaces.</summary>
    private void BuildBlizzardExterior()
    {
        var scenery=_scenery!;
        const float ground=-.325f;
        var snow=_snowMaterial;var white=_art.Color(new("f4f8fb"),.6f);
        var shadowSnow=_art.Color(new("dbe6f0"),.75f);

        // Pine groves frame the rear and both sides.
        var random=new Random(31);
        foreach(var (x0,x1,z0,z1,count) in new[]{(-9f,Width+9f,-7.5f,-2.2f,15),(-8f,-2.2f,-1f,Depth-3f,7),(Width+2.2f,Width+8f,-1f,Depth-5f,6)})
            for(int i=0;i<count;i++)
            {
                float x=Mathf.Lerp(x0,x1,(i+(float)random.NextDouble()*.6f)/count);
                float z=Mathf.Lerp(z0,z1,(float)random.NextDouble());
                if(x0<0&&x1<0)(x,z)=(Mathf.Lerp(x0,x1,(float)random.NextDouble()),Mathf.Lerp(z0,z1,(i+.5f)/count));
                if(x0>Width)(x,z)=(Mathf.Lerp(x0,x1,(float)random.NextDouble()),Mathf.Lerp(z0,z1,(i+.5f)/count));
                PineTree(scenery,new(x,ground,z),.95f+(float)random.NextDouble()*.75f,true,i*1.7f);
            }

        // An igloo with a block pattern and a dark doorway, beyond the north-east corner.
        var igloo=Prop(scenery,"Igloo",new(Width+3.4f,ground,-1.2f),-.7f);
        var dome=Sphere(igloo,Vector3.Zero,new(2.6f,2.1f,2.6f),white);dome.Name="IglooDome";
        for(int ring=1;ring<4;ring++)
        {
            float y=ring*.24f,radius=1.3f*Mathf.Sqrt(1-Mathf.Pow(y/1.05f,2));
            Taper(igloo,new(0,y,0),radius+.012f,radius+.012f,.025f,shadowSnow,24);
        }
        Taper(igloo,new(0,.32f,1.25f),.48f,.48f,.7f,white,16).Rotation=new(Mathf.Pi/2,0,0);
        Sphere(igloo,new(0,.3f,1.58f),new(.6f,.52f,.08f),_art.Color(new("27354a")));

        // A cheerful snowman watching from outside the front-left corner.
        var snowman=Prop(scenery,"Snowman",new(-2.3f,ground,Depth-1.6f),.45f);
        Sphere(snowman,new(0,.42f,0),Vector3.One*.9f,white);
        Sphere(snowman,new(0,1.05f,0),Vector3.One*.65f,white);
        Sphere(snowman,new(0,1.52f,0),Vector3.One*.46f,white);
        var coal=_art.Color(new("26262c"),.5f);
        foreach(float x in new[]{-.08f,.08f})Bead(snowman,new(x,1.58f,.2f),Vector3.One*.06f,coal);
        for(int b=0;b<3;b++)Bead(snowman,new(0,.9f+b*.15f,.31f-b*.01f),Vector3.One*.06f,coal);
        Cone(snowman,new(0,1.52f,.32f),.05f,.26f,_art.Color(new("f08a2c"),.6f),8).Rotation=new(Mathf.Pi/2,0,0);
        Taper(snowman,new(0,1.8f,0),.18f,.18f,.26f,_art.Color(new("3a3d52"),.6f),14);
        Cylinder(snowman,new(0,1.69f,0),.27f,.04f,_art.Color(new("3a3d52"),.6f));
        Box(snowman,new(0,1.32f,.02f),new(.62f,.1f,.6f),_art.Color(new("d8433b"),.8f),.04f);
        foreach(int side in new[]{-1,1})
            Box(snowman,new(side*.48f,1.15f,0),new(.5f,.04f,.04f),_art.Color(new("6b4d33"))).Rotation=new(0,0,side*.45f);

        // A frozen pond with penguins along the east side.
        var pond=Prop(scenery,"FrozenPond",new(Width+3.2f,ground,9.5f));
        Cylinder(pond,new(0,.01f,0),2.1f,.04f,_art.Color(new("9fd3ea"),.08f));
        Cylinder(pond,new(0,0,0),2.35f,.035f,shadowSnow);
        foreach(var (x,z,phase) in new[]{(-.6f,-2.6f,0f),(.5f,-2.4f,1.3f),(1.9f,1.9f,2.6f)})Penguin(scenery,pond.Position+new Vector3(x,0,z),phase);

        // Ice crystal clusters and soft drifts hug the outside of the walls.
        var crystal=_crystalMaterial;
        foreach(var (x,z,scale) in new[]{(-1.7f,4f,.55f),(-1.9f,12.5f,.45f),(Width+1.7f,15f,.5f),(Width*.25f,-1.6f,.5f),(Width*.62f,-1.7f,.45f)})
            for(int shard=0;shard<3;shard++)
            {
                var ice=new MeshInstance3D{Name="IceCrystal",Mesh=_iceCones[shard],MaterialOverride=crystal,
                    Position=new(x+(shard-1)*.35f,ground,z+(shard%2)*.3f),Scale=Vector3.One*scale*(1-shard*.22f),Rotation=new(.15f*(shard-1),shard*1.7f,.1f*(shard-1))};
                scenery.AddChild(ice);
            }
        foreach(var (x,z,w) in new[]{(-1.2f,8f,2.4f),(Width+1.2f,4f,2.2f),(Width*.45f,-1.2f,3f),(-1.3f,Depth-4.5f,1.8f),(Width+1.3f,Depth-2.5f,2f)})
            Sphere(scenery,new(x,ground,z),new(w*.55f,.7f,w),snow).Name="Snowdrift";

        // Far glacier peaks above the forest.
        foreach(var (x,z,radius,height) in new[]{(-6f,-26f,9f,13f),(Width*.45f,-34f,13f,18f),(Width+8f,-27f,10f,14f),(Width*.15f,-40f,11f,15f)})
        {
            var peak=Prop(scenery,"GlacierPeak",new(x,ground,z));
            Cone(peak,new(0,height/2,0),radius,height,_art.Color(new("8ea3bd"),.9f),9).CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            Cone(peak,new(0,height*.8f,0),radius*.42f,height*.4f+.05f,_art.Color(new("f2f7fb"),.7f),9).CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    private void Penguin(Node3D parent,Vector3 position,float phase)
    {
        var root=Prop(parent,"Penguin",position,phase*2.2f);
        var body=new Node3D();root.AddChild(body);
        var black=_art.Toy(new("2a2f3d"),.45f);var white=_art.Toy(new("f6f5ef"),.5f);var orange=_art.Toy(new("f29a2e"),.5f);
        Sphere(body,new(0,.32f,0),new(.4f,.6f,.36f),black);
        Sphere(body,new(0,.3f,.07f),new(.3f,.48f,.26f),white);
        Sphere(body,new(0,.68f,0),new(.28f,.27f,.27f),black);
        foreach(float x in new[]{-.06f,.06f})Bead(body,new(x,.72f,.12f),Vector3.One*.05f,white);
        Cone(body,new(0,.66f,.17f),.04f,.1f,orange,8).Rotation=new(Mathf.Pi/2,0,0);
        foreach(float x in new[]{-.1f,.1f})Sphere(root,new(x,.02f,.08f),new(.12f,.04f,.18f),orange);
        var flippers=new Node3D[2];
        for(int side=0;side<2;side++)
        {
            float sign=side==0?-1:1;
            flippers[side]=new Node3D{Position=new(sign*.19f,.5f,0)};body.AddChild(flippers[side]);
            Sphere(flippers[side],new(sign*.03f,-.13f,0),new(.06f,.3f,.14f),black);
        }
        _sceneryMotion.Add(t=>
        {
            float waddle=Mathf.Sin(t*5+phase);
            body.Rotation=new(0,0,waddle*.12f);
            float flap=Mathf.Max(0,Mathf.Sin(t*.8f+phase*3))*Mathf.Abs(Mathf.Sin(t*12));
            flippers[0].Rotation=new(0,0,-.15f-flap*.7f);flippers[1].Rotation=new(0,0,.15f+flap*.7f);
        });
    }
}
