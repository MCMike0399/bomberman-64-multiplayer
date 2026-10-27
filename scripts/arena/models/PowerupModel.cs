using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Models;

/// <summary>
/// Distinct silhouettes communicate the reward before the player touches it. Each pickup is a
/// small lacquered toy on a stamped coin: the badge color names its family, faces share the
/// bomber's simple eyes, and a per-kind idle motion gives it a temperament.
/// </summary>
public partial class PowerupModel : Node3D
{
    public const float VisualScale=1.20f;
    public const float IconScale=1.30f;
    private const float IconLift=.16f;
    // An idle sway keeps faces toward the battle camera; a periodic twirl still reads as a pickup.
    private const float TwirlPeriod=4.2f,TwirlSeconds=.7f,PopSeconds=.35f;
    private readonly Node3D _icon=new(),_pose=new();
    private readonly List<(MeshInstance3D Eye,Vector3 Scale)> _eyes=[];
    private Action<float>? _animate;
    private float _time,_phase=-1;
    public string Kind {get;}
    public PowerupModel(ArtMaterials art,string kind)
    {
        Kind=kind;Scale=Vector3.One*VisualScale;
        _icon.Name="Icon";_icon.Scale=Vector3.One*IconScale;_icon.Position=new(0,IconLift,0);AddChild(_icon);
        _pose.Name="Pose";_icon.AddChild(_pose);
        var white=art.Toy(new("fff9dc"),.4f);var ink=art.Toy(new("253453"),.35f);var gold=art.Toy(new("ffd653"),.4f,true);
        var eyeInk=art.Toy(new("272341"),.55f);var eyeCream=art.Toy(new("fff9dc"),.5f);
        Cylinder(this,new(0,-.3f,0),.3f,.05f,gold);
        // The coin sits on an ink lip like the menu's stamped keys; the inset badge names the family.
        var stand=new Node3D {Name="Stand"};AddChild(stand);
        Detail(Cylinder(stand,new(0,-.33f,0),.33f,.05f,art.Toy(new("193560"),.5f)));
        Detail(Cylinder(stand,new(0,-.271f,0),.215f,.012f,art.Toy(new(kind switch
            {"fire" or "bomb"=>"ef5a4f","heart" or "focus"=>"4cc3c8","tiny" or "haste"=>"59bf8c","big" or "slow"=>"9d56ce",_=>"3d5aa8"}),.45f,true)));
        switch(kind)
        {
            case "fire":
            {
                var orange=art.Toy(new("ff7438"),.45f,true);var amber=art.Toy(new("ffb93c"),.4f,true);
                // A plump teardrop: round body, a narrower crown and a licking tip, with side tongues.
                var flame=Pivot(_pose,new(0,-.22f,0));
                Sphere(flame,new(0,.17f,0),new(.36f,.34f,.3f),orange);
                var crown=Pivot(flame,new(0,.3f,0));
                Sphere(crown,new(0,.03f,0),new(.24f,.26f,.21f),art.Toy(new("ff9440"),.45f,true));
                var tip=Pivot(crown,new(0,.13f,0));Cone(tip,new(0,.1f,0),.085f,.2f,amber);
                var tongues=new Node3D[2];
                for(int i=0;i<2;i++)
                {
                    float side=i*2-1;
                    tongues[i]=Pivot(flame,new(side*.15f,.2f,0));tongues[i].Rotation=new(0,0,-side*.55f);
                    Cone(tongues[i],new(0,.085f,0),.06f,.17f,amber);
                }
                foreach(float side in new[]{-1f,1f})Eye(flame,new(side*.062f,.2f,.14f),new(.052f,.1f,.04f),eyeInk);
                _animate=t=>
                {
                    flame.Scale=new(1+Mathf.Sin(t*9)*.03f,1+Mathf.Sin(t*7.3f)*.04f,1);
                    crown.Rotation=new(0,0,Mathf.Sin(t*4.3f)*.08f);crown.Scale=new(1,1+Mathf.Sin(t*8.7f)*.06f,1);
                    tip.Scale=new(1,1+Mathf.Sin(t*11)*.2f,1);tip.Rotation=new(0,0,Mathf.Sin(t*5.1f)*.18f);
                    for(int i=0;i<2;i++)tongues[i].Scale=new(1,1+Mathf.Sin(t*13+i*2.2f)*.22f,1);
                };
                break;
            }
            case "heart":
            {
                var pink=art.Toy(new("f65391"),.4f);var blush=art.Toy(new("ffa6c9"),.6f);
                var heart=Pivot(_pose,new(0,.02f,0));
                foreach(float side in new[]{-1f,1f})
                {
                    Sphere(heart,new(side*.105f,.06f,0),new(.25f,.25f,.19f),pink);
                    Eye(heart,new(side*.07f,.07f,.092f),new(.045f,.088f,.035f),eyeInk);
                    Detail(Sphere(heart,new(side*.13f,-.02f,.07f),new(.07f,.035f,.025f),blush));
                }
                RoundedBox(heart,new(0,-.07f,-.01f),new(.24f,.24f,.16f),pink,.4f).RotationDegrees=new(0,0,45);
                // Lub-dub: two quick beats, then a rest.
                _animate=t=>
                {
                    float p=Mathf.PosMod(t,1.1f);
                    float beat=Mathf.Exp(-Mathf.Pow((p-.08f)/.05f,2))*.13f+Mathf.Exp(-Mathf.Pow((p-.28f)/.06f,2))*.08f;
                    heart.Scale=new(1+beat,1+beat*.8f,1+beat);
                };
                break;
            }
            case "remote":
            {
                var body=Pivot(_pose,new(0,-.01f,0));
                RoundedBox(body,Vector3.Zero,new(.3f,.42f,.17f),art.Toy(new("2478e8"),.35f),.55f);
                RoundedBox(body,new(0,-.02f,.075f),new(.22f,.3f,.04f),white,.5f);
                Cylinder(body,new(0,.04f,.1f),.068f,.035f,art.Toy(new("ed4b55"),.4f)).RotationDegrees=new(90,0,0);
                foreach(float side in new[]{-1f,1f})Detail(Sphere(body,new(side*.05f,-.1f,.098f),new(.045f,.045f,.03f),ink));
                Cylinder(body,new(.08f,.27f,0),.018f,.16f,ink);
                // The antenna lamp blinks the remote bomb's green command light.
                var lamp=Pivot(body,new(.08f,.37f,0));Sphere(lamp,Vector3.Zero,Vector3.One*.085f,art.Toy(new("a9ed69"),.35f,true));
                _animate=t=>
                {
                    lamp.Scale=Vector3.One*(Mathf.PosMod(t,.9f)<.45f?1.2f:.75f);
                    body.Rotation=new(0,0,Mathf.Sin(t*2.2f)*.08f);
                };
                break;
            }
            case "bounce":
            {
                var spring=Pivot(_pose,new(0,-.23f,0));
                for(int i=0;i<3;i++)Cylinder(spring,new(0,.03f+i*.07f,0),.15f-i*.02f,.035f,white);
                var ball=Pivot(_pose,new(0,.12f,0));
                Sphere(ball,Vector3.Zero,Vector3.One*.3f,art.Toy(new("a365ea"),.35f));
                // Closed, happy ^ ^ eyes.
                foreach(float side in new[]{-1f,1f})foreach(float stroke in new[]{-1f,1f})
                    Detail(Box(ball,new(side*.06f+stroke*.018f,.03f,.135f),new(.045f,.02f,.02f),eyeInk,.008f)).Rotation=new(0,0,-stroke*.7f);
                _animate=t=>
                {
                    float hop=Mathf.Abs(Mathf.Sin(t*3.6f)),squash=Mathf.Pow(1-hop,6)*.22f;
                    ball.Position=new(0,.07f+hop*.13f,0);ball.Scale=new(1+squash,1-squash,1+squash);
                    spring.Scale=new(1,.65f+hop*.35f,1);
                };
                break;
            }
            case "focus":
            {
                var cyan=art.Toy(new("70e6ff"),.35f,true);
                var reticle=Pivot(_pose,new(0,.07f,0));
                var corners=new (Node3D Node,Vector2 Direction)[4];
                for(int i=0;i<4;i++)
                {
                    var direction=new Vector2(i%2*2-1,i/2*2-1);
                    var corner=Pivot(reticle,new(direction.X*.18f,direction.Y*.18f,0));corners[i]=(corner,direction);
                    Box(corner,new(-direction.X*.04f,0,0),new(.13f,.055f,.07f),cyan,.014f);
                    Box(corner,new(0,-direction.Y*.04f,0),new(.055f,.13f,.07f),cyan,.014f);
                }
                var ring=Pivot(reticle,Vector3.Zero);
                for(int i=0;i<4;i++)Detail(Box(ring,new(Mathf.Cos(i*Mathf.Pi/2)*.26f,Mathf.Sin(i*Mathf.Pi/2)*.26f,0),new(.05f,.05f,.05f),white,.012f));
                Sphere(reticle,Vector3.Zero,new(.21f,.21f,.09f),white);
                var dot=Pivot(reticle,new(0,0,.04f));Sphere(dot,Vector3.Zero,Vector3.One*.12f,cyan);
                // The brackets close on the target as a charge locks on.
                _animate=t=>
                {
                    float focus=.5f+.5f*Mathf.Sin(t*3.2f);
                    foreach(var (node,direction) in corners)node.Position=new(direction.X*(.18f-.03f*focus),direction.Y*(.18f-.03f*focus),0);
                    ring.Rotation=new(0,0,-t*1.5f);dot.Scale=Vector3.One*(1+.18f*focus);
                };
                break;
            }
            case "tiny":case "big":
            {
                bool tiny=kind=="tiny";float d=tiny?.3f:.46f;
                var tint=art.Toy(new(tiny?"70e6ff":"f6a156"),.35f,true);
                // A miniature bomber head, shrunk or swollen, with chevrons that say which way.
                var head=Pivot(_pose,new(0,tiny?-.06f:0,0));head.Rotation=new(-.35f,0,0);
                Sphere(head,Vector3.Zero,new(d,d*.94f,d*.92f),white);
                Sphere(head,new(0,-d*.04f,d*.32f),new(d*.6f,d*.46f,d*.4f),art.Toy(new("fbd2a6"),.7f));
                foreach(float side in new[]{-1f,1f})Eye(head,new(side*d*.12f,0,d*.505f),new(d*.085f,d*.19f,d*.06f),eyeInk);
                Cylinder(head,new(0,d*.52f,0),d*.045f,d*.16f,ink);
                Sphere(head,new(0,d*.64f,0),Vector3.One*d*.24f,art.Toy(new("ee4a90"),.42f));
                var arrows=new Node3D[2];float reach=d*.5f+(tiny?.1f:.07f);
                for(int i=0;i<2;i++)
                {
                    float side=i*2-1,point=tiny?-side:side;
                    arrows[i]=Pivot(_pose,new(side*reach,head.Position.Y,0));
                    foreach(float stroke in new[]{-1f,1f})
                        Box(arrows[i],new(-point*.02f,stroke*.03f,0),new(.1f,.035f,.04f),tint,.01f).Rotation=new(0,0,-point*stroke*.7f);
                }
                _animate=t=>
                {
                    float breath=.5f+.5f*Mathf.Sin(t*4);
                    head.Scale=Vector3.One*(tiny?1-.07f*breath:1+.06f*breath);
                    for(int i=0;i<2;i++)arrows[i].Position=new((i*2-1)*(reach+(tiny?-.03f:.03f)*breath),head.Position.Y,0);
                };
                break;
            }
            case "haste":
            {
                var yellow=art.Toy(new("ffc83d"),.4f);
                // A winged sneaker in profile, rocking heel to toe as it runs in place.
                var boot=Pivot(_pose,new(.03f,-.2f,0));
                RoundedBox(boot,new(.02f,.035f,0),new(.44f,.07f,.24f),white,.6f);
                RoundedBox(boot,new(.04f,.12f,0),new(.4f,.14f,.23f),yellow,.7f);
                Sphere(boot,new(.17f,.1f,0),new(.15f,.13f,.235f),white);
                RoundedBox(boot,new(-.07f,.29f,0),new(.2f,.26f,.21f),yellow,.6f);
                RoundedBox(boot,new(-.07f,.42f,0),new(.24f,.06f,.24f),white,.6f);
                var wings=new Node3D[2];
                for(int i=0;i<2;i++)
                {
                    wings[i]=Pivot(boot,new(-.16f,.33f,(i*2-1)*.12f));
                    Sphere(wings[i],new(-.07f,.04f,0),new(.18f,.08f,.03f),white).Rotation=new(0,0,.45f);
                    Sphere(wings[i],new(-.06f,-.02f,0),new(.13f,.06f,.03f),white).Rotation=new(0,0,.2f);
                }
                var streaks=new MeshInstance3D[3];
                for(int i=0;i<3;i++)streaks[i]=Detail(Box(_pose,new(-.36f,-.08f+i*.1f,0),new(.16f,.03f,.04f),white,.01f));
                _animate=t=>
                {
                    boot.Rotation=new(0,0,Mathf.Sin(t*10)*.12f);
                    for(int i=0;i<2;i++)wings[i].Rotation=new((i*2-1)*(.3f+.35f*Mathf.Sin(t*16)),0,0);
                    for(int i=0;i<3;i++)
                    {
                        float flow=Mathf.PosMod(t*1.6f+i*.37f,1);
                        streaks[i].Position=new(-.3f-flow*.14f,streaks[i].Position.Y,0);streaks[i].Scale=new(1-flow*.6f,1,1);
                    }
                };
                break;
            }
            case "slow":
            {
                var lilac=art.Toy(new("cbbff2"),.5f);
                // A snail: the shell spiral faces the camera; eye stalks wobble as it creeps.
                var snail=Pivot(_pose,new(0,-.2f,0));
                var foot=RoundedBox(snail,new(.02f,.04f,0),new(.5f,.09f,.2f),lilac,.95f);
                var neck=Pivot(snail,new(.17f,.06f,0));
                Sphere(neck,new(.03f,.08f,0),new(.15f,.22f,.15f),lilac);
                var stalks=new Node3D[2];
                for(int i=0;i<2;i++)
                {
                    stalks[i]=Pivot(neck,new(.05f,.17f,(i*2-1)*.04f));
                    Cylinder(stalks[i],new(0,.06f,0),.016f,.12f,lilac);
                    Sphere(stalks[i],new(0,.13f,0),Vector3.One*.075f,eyeCream);
                    Eye(stalks[i],new(.012f,.13f,.032f),new(.035f,.045f,.02f),eyeInk);
                }
                var shell=Pivot(snail,new(-.05f,.2f,0));
                Sphere(shell,Vector3.Zero,new(.36f,.34f,.26f),art.Toy(new("9b92e7"),.35f,true));
                Sphere(shell,new(.02f,.01f,.085f),new(.22f,.21f,.1f),art.Toy(new("c8c1f7"),.4f));
                Sphere(shell,new(.035f,.02f,.125f),new(.1f,.1f,.05f),art.Toy(new("6f63c9"),.4f));
                _animate=t=>
                {
                    foot.Scale=new(1+Mathf.Sin(t*1.5f)*.06f,1,1);
                    neck.Rotation=new(0,0,Mathf.Sin(t*1.5f)*.08f);
                    shell.Position=new(-.05f,.2f+Mathf.Sin(t*1.5f+1)*.012f,0);
                    for(int i=0;i<2;i++)stalks[i].Rotation=new((i*2-1)*.25f,0,-.25f+Mathf.Sin(t*2.4f+i*1.3f)*.18f);
                };
                break;
            }
            default:
            {
                // Bomb Up, Power bomb and any unknown kind share the bomb body.
                bool red=kind=="red";
                var body=Pivot(_pose,Vector3.Zero);
                Sphere(body,Vector3.Zero,Vector3.One*.4f,red?art.Toy(new("e8384e"),.32f):ink);
                Cylinder(body,new(0,.205f,0),.06f,.06f,art.Toy(new("ac8b3a"),.35f));
                Cylinder(body,new(.03f,.27f,0),.022f,.12f,art.Toy(new("e7c989"),.5f)).RotationDegrees=new(0,0,-24);
                // A power bomb's spark burns the blue of its blast.
                var spark=Pivot(body,new(.06f,.34f,0));Detail(Sphere(spark,Vector3.Zero,Vector3.One*.1f,art.Toy(new(red?"6ec8ff":"ffcf51"),.3f,true)));
                foreach(float side in new[]{-1f,1f})
                {
                    Eye(body,new(side*.07f,red?.02f:.03f,.18f),new(.06f,.11f,.04f),eyeCream);
                    if(red)Detail(Box(body,new(side*.075f,.12f,.145f),new(.09f,.028f,.03f),eyeInk,.01f)).Rotation=new(0,0,side*.35f);
                }
                Node3D? plus=null;
                if(kind=="bomb")
                {
                    plus=Pivot(_pose,new(.25f,.2f,.05f));
                    Box(plus,Vector3.Zero,new(.15f,.05f,.05f),white,.015f);Box(plus,Vector3.Zero,new(.05f,.15f,.05f),white,.015f);
                }
                _animate=t=>
                {
                    spark.Scale=Vector3.One*(1+Mathf.Sin(t*30)*.25f);
                    float throb=red?Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*6)),4)*.06f:Mathf.Sin(t*5)*.03f;
                    body.Scale=new(1+throb,1-throb,1+throb);
                    if(plus!=null)plus.Scale=Vector3.One*(1+.15f*Mathf.Max(0,Mathf.Sin(t*4)));
                };
                break;
            }
        }
    }
    public override void _Process(double delta)
    {
        _time+=(float)delta;
        // Neighboring pickups twirl at different moments; derive the offset from where this one rests.
        if(_phase<0)_phase=Mathf.PosMod((IsInsideTree()?GlobalPosition:Position).Dot(new(.37f,0,.61f)),1)*TwirlPeriod;
        float t=_time+_phase,cycle=Mathf.PosMod(t,TwirlPeriod);
        _icon.Position=new(0,IconLift+Mathf.Sin(_time*3)*.075f,0);
        _icon.Rotation=new(0,Mathf.Sin(t*1.6f)*.42f+(cycle<TwirlSeconds?Mathf.SmoothStep(0,1,cycle/TwirlSeconds)*Mathf.Tau:0),0);
        // Ease-out-back pop when the reward first appears.
        float pop=Mathf.Min(1,_time/PopSeconds)-1;
        _pose.Scale=Vector3.One*(1+2.70158f*pop*pop*pop+1.70158f*pop*pop);
        bool blink=Mathf.PosMod(t*1.3f,3.7f)<.13f;
        foreach(var (eye,scale) in _eyes)eye.Scale=blink?new(scale.X,scale.Y*.15f,scale.Z):scale;
        _animate?.Invoke(t);
    }
    private static Node3D Pivot(Node3D parent,Vector3 position)
    {
        var node=new Node3D {Position=position};parent.AddChild(node);return node;
    }
    // Small facial and trim details skip the shadow pass; the silhouette already grounds the pickup.
    private static MeshInstance3D Detail(MeshInstance3D mesh)
    {
        mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;return mesh;
    }
    private void Eye(Node3D parent,Vector3 position,Vector3 scale,Material material)=>_eyes.Add((Detail(Sphere(parent,position,scale,material)),scale));
}
