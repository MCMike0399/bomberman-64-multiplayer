using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
namespace BlastCircuit.Arena.Models;

/// <summary>Articulated original mesh character, animated from authoritative movement and action state.</summary>
public partial class BomberModel : Node3D
{
    private readonly Node3D _body=new(),_leftArm=new(),_rightArm=new(),_leftLeg=new(),_rightLeg=new();
    private readonly Node3D _upperBody=new(),_upperParts=new();
    private Node3D _stars=new();
    private Label3D _nameLabel=null!;
    public float StatusScale {get;set;}=1;
    private bool _carrying,_stunned,_ghost;
    private bool _heldVictim,_airborne;
    private float _downBlend;
    public float KnockdownPose=>_downBlend;
    public void RecoveryPose(bool held,bool airborne){_heldVictim=held;_airborne=airborne;}
    private float _dropPulse;
    public float PlacementPose=>_dropPulse;
    private float _time,_walkPhase,_speed,_carryBlend,_pumpPulse,_throwPulse,_kickPulse,_liftPulse,_deathPulse,_victoryPulse;
    private int _charge;
    private readonly ArtMaterials _art;
    private readonly List<MeshInstance3D> _suitParts=[],_torsoParts=[];
    private readonly Node3D _head=new();
    private MeshInstance3D _leftBoot=null!,_rightBoot=null!;
    private MeshInstance3D _leftStem=null!,_rightStem=null!;
    private readonly Node3D _antenna=new();
    private MeshInstance3D _antennaBall=null!;
    private static readonly Vector3 BootScale=new(.315f,.25f,.38f);
    // Secondary motion is simulated rather than phase-locked: limbs, head and
    // antenna carry momentum, lag the beat and overshoot when the actor stops.
    private Spring _leftArmSwing,_rightArmSwing,_lean,_bank,_headBob,_headRoll,_headTurn,_antennaPitch,_antennaRoll,_antennaBob;
    private float _lastYaw;
    private bool _yawKnown;
    // One complete left/right stride. Short legs need a quick scamper: at full
    // speed each boot sweeps back about as fast as the root travels, so the
    // planted sole grips the floor instead of skating under a gliding body.
    // A planted boot can only sweep as far as a rigid stubby leg reaches, so the
    // stance is short and bouncy and the hip slides a little under the belt.
    private const float StrideLength=1.5f,Plant=.36f,LegSwing=.62f,HipSlide=.065f,TuckHeight=.055f,RunLean=.13f;
    private const float HeadCenter=1.085f,HelmetHalfHeight=.375f,HelmetDepth=.405f,HelmetForward=.045f,FaceCenterY=-.010f,EyeCenterY=.010f;
    private static ArrayMesh? _helmetMesh,_torsoMesh,_faceMesh,_faceRimMesh;
    private static readonly Dictionary<float,ArrayMesh> EyeMeshes=[];
    private readonly List<MeshInstance3D> _eyes=[];
    private Color _suitColor;
    public int PumpAnimationCount {get;private set;}
    public float WalkPose=>_leftLeg.Rotation.X;
    public float WalkBodyHeight=>_body.Position.Y;
    public Vector3 WalkBodyOffset=>_body.Position;
    public Vector3 WalkBodyRotation=>_body.Rotation;
    public Vector3 WalkHeadRotation=>_head.Rotation;
    public Vector3 WalkShoulderRotation=>_upperBody.Rotation;
    public float WalkCompression=>1-_body.Scale.Y;
    public float LeftFootLift=>(_body.Transform*(_leftLeg.Transform*_leftBoot.Position)).Y-.145f;
    public float RightFootLift=>(_body.Transform*(_rightLeg.Transform*_rightBoot.Position)).Y-.145f;
    public Quaternion LeftBootOrientation=>new Quaternion((_body.Transform*_leftLeg.Transform*_leftBoot.Transform).Basis.Orthonormalized());
    public Quaternion RightBootOrientation=>new Quaternion((_body.Transform*_rightLeg.Transform*_rightBoot.Transform).Basis.Orthonormalized());
    public Vector3 LeftBootCenter=>_body.Transform*(_leftLeg.Transform*_leftBoot.Position);
    public Vector3 RightBootCenter=>_body.Transform*(_rightLeg.Transform*_rightBoot.Position);
    public float WalkLegStretch=>Mathf.Max(_leftStem.Scale.Y,_rightStem.Scale.Y)/.19f;
    /// <summary>Distance a leg hangs below or beside its hip. Tucking a passing boot up into the suit, or sliding it fore/aft under the belt, never exposes a gap.</summary>
    public float WalkHipSeparation=>Mathf.Max(HipGap(_leftLeg.Position-new Vector3(-.15f,.37f,0)),HipGap(_rightLeg.Position-new Vector3(.15f,.37f,0)));
    private static float HipGap(Vector3 offset)=>new Vector2(offset.X,Mathf.Min(0,offset.Y)).Length();
    public float WalkHipSlide=>Mathf.Max(Mathf.Abs(_leftLeg.Position.Z),Mathf.Abs(_rightLeg.Position.Z));
    public float WalkLegTuck=>Mathf.Max(_leftLeg.Position.Y,_rightLeg.Position.Y)-.37f;
    public Vector3 AntennaRotation=>_antenna.Rotation;
    private bool _kickWinding;
    private float _kickWindupRemaining;
    public float KickPose=>_kickWinding?-(1-Mathf.Clamp(_kickWindupRemaining/(4f/30f),0,1)):_kickPulse;
    public void KickWindup(float remaining){_kickWinding=remaining>0;_kickWindupRemaining=remaining;}
    public float CarryBlend=>_carryBlend;
    public float PumpIntensity=>Mathf.Sin(_pumpPulse*Mathf.Pi);
    public Vector3 BombSocket=>_upperParts.ToGlobal(new Vector3(0,Mathf.Lerp(.25f,1.47f,_carryBlend)+Mathf.Sin(_pumpPulse*Mathf.Pi)*.09f,.08f));
    public void Pump(int charge){_charge=charge;_pumpPulse=1;PumpAnimationCount++;}
    public string DisplayName=>_nameLabel.Text;
    public void ShowName(bool visible)=>_nameLabel.Visible=visible;
    public void SetSuitColor(Color color)
    {
        if(_suitColor==color)return;
        _suitColor=color;
        foreach(var part in _suitParts)part.MaterialOverride=_art.Toy(color,.46f);
        foreach(var part in _torsoParts)part.MaterialOverride=_art.Toy(TorsoColor(color),.46f);
    }
    private static Color TorsoColor(Color color)=>color.R>.85f&&color.G>.85f&&color.B>.8f?new Color("315dc5"):color;
    /// <summary>Accepted actions briefly animate joints without moving the authoritative character root.</summary>
    public void React(string kind)
    {
        switch(kind)
        {
            case "bomb-drop":_dropPulse=1;break;
            case "kick":_kickWinding=false;_kickPulse=1;break;
            case "kick-cancel":_kickWinding=false;_kickPulse=0;break;
            case "lift":_liftPulse=1;break;
            case "throw":_throwPulse=1;break;
            case "pump":_pumpPulse=1;break;
            case "death":_deathPulse=1;break;
            case "knockdown":case "player-land":_impactPulse=1;break;
            case "recover":case "player-release":_recoverPulse=1;break;
            case "player-grab":_liftPulse=1;break;
            case "win":case "victory":_victoryPulse=1;break;
        }
    }
    public BomberModel(ArtMaterials art,Color color,string name)
    {
        _art=art;_suitColor=color;
        _time=(color.R*3.1f+color.G*5.3f+color.B*7.7f)%4.7f;
        var suit=art.Toy(color,.46f);var white=art.Toy(new("fff7e9"),.48f);var pink=art.Toy(new("ee4a90"),.42f);
        var face=art.Toy(new("ffe0b7"),.78f);var dark=art.Toy(new("272341"),.55f);var gold=art.Toy(new("ffd15a"),.36f);
        AddChild(_body);
        // Upper-body pivot at the waist: shoulders can follow the planted hips
        // without dragging the short legs or rotating the helmet around the floor.
        _upperBody.Position=new(0,.37f,0);_upperParts.Position=new(0,-.37f,0);
        _body.AddChild(_upperBody);_upperBody.AddChild(_upperParts);
        // The suit is a compact pear: tucked shoulders over a slightly fuller waist.
        var torso=new MeshInstance3D {Name="Torso",Mesh=_torsoMesh??=CreateHelmet(false),MaterialOverride=art.Toy(TorsoColor(color),.46f),
            Position=new(0,.53f,0),Scale=new(.395f/.854f,.38f/.79f,.37f/.73f),Rotation=new(0,0,Mathf.Pi)};
        _upperParts.AddChild(torso);_torsoParts.Add(torso);
        _torsoParts.Add(Sphere(_upperParts,new(0,.722f,0),new(.16f,.065f,.16f),art.Toy(TorsoColor(color),.46f)));
        RoundedBox(_upperParts,new(0,.405f,.015f),new(.395f,.085f,.34f),dark);
        RoundedBox(_upperParts,new(0,.41f,.195f),new(.13f,.12f,.055f),gold,.35f);
        RoundedBox(_upperParts,new(0,.41f,.227f),new(.066f,.06f,.012f),dark,.2f);
        // A softly rounded helmet sits over a slimmer suit; the shallow face remains easy to read.
        _head.Position=new(0,HeadCenter,0);_upperParts.AddChild(_head);
        var helmet=new MeshInstance3D {Name="HelmetShell",Mesh=_helmetMesh??=CreateHelmet(),MaterialOverride=suit};
        _head.AddChild(helmet);_suitParts.Add(helmet);
        _head.AddChild(new MeshInstance3D {Name="FaceRim",Mesh=_faceRimMesh??=CreateCurvedFacePatch(.646f,.466f,0,FaceCenterY,.009f),MaterialOverride=art.Toy(new("8b755c"),.8f),Position=new(0,FaceCenterY,0)});
        _head.AddChild(new MeshInstance3D {Name="FaceSurface",Mesh=_faceMesh??=CreateCurvedFacePatch(.634f,.454f,0,FaceCenterY,.016f),MaterialOverride=face,Position=new(0,FaceCenterY,0)});
        foreach(float x in new[]{-.134f,.134f})
        {
            // Keep two simple eyes laid against the faceplate's moderate curvature.
            if(!EyeMeshes.TryGetValue(x,out var eyeMesh))EyeMeshes[x]=eyeMesh=CreateCurvedFacePatch(.036f,.193f,x,EyeCenterY,.022f,.8f,false);
            var eye=new MeshInstance3D {Mesh=eyeMesh,MaterialOverride=dark,Position=new(x,EyeCenterY,0)};
            _head.AddChild(eye);_eyes.Add(eye);
        }
        BuildMishapFace(dark);
        // The antenna pivots just inside the crown so its springy wobble never opens a gap.
        _antenna.Position=new(0,HelmetHalfHeight-.03f,HelmetForward);_head.AddChild(_antenna);
        Cylinder(_antenna,new(0,.032f,0),.058f,.1f,dark);
        _antennaBall=Sphere(_antenna,new(0,.116f,0),new(.225f,.225f,.225f),pink);
        _leftArm.Position=new(-.25f,.68f,0);_rightArm.Position=new(.25f,.68f,0);
        _upperParts.AddChild(_leftArm);_upperParts.AddChild(_rightArm);
        foreach(var arm in new[]{_leftArm,_rightArm})
        {
            Sphere(arm,new(0,-.073f,0),new(.13f,.19f,.13f),white);
            RoundedBox(arm,new(0,-.155f,.015f),new(.165f,.04f,.165f),pink,1);
            Sphere(arm,new(0,-.22f,.05f),new(.23f,.225f,.245f),pink);
            Sphere(arm,new(arm==_leftArm?.09f:-.09f,-.21f,.12f),new(.105f,.14f,.12f),pink);
        }
        _leftLeg.Position=new(-.15f,.37f,0);_rightLeg.Position=new(.15f,.37f,0);_body.AddChild(_leftLeg);_body.AddChild(_rightLeg);
        foreach(var leg in new[]{_leftLeg,_rightLeg})
        {
            var stem=Sphere(leg,new(0,-.075f,0),new(.15f,.19f,.15f),white);
            if(leg==_leftLeg)_leftStem=stem;else _rightStem=stem;
            float outward=leg==_leftLeg?-.24f:.17f;
            var boot=Sphere(leg,new(outward*.11f,-.225f,leg==_leftLeg?.055f:.045f),new(.315f,.25f,.38f),pink);boot.Rotation=new(0,outward,0);
            if(leg==_leftLeg)_leftBoot=boot;else _rightBoot=boot;
        }
        AddChild(_stars);
        for(int i=0;i<3;i++)
        {
            float a=Mathf.Tau*i/3;var star=Box(_stars,new(Mathf.Cos(a)*.42f,0,Mathf.Sin(a)*.42f),new(.13f,.13f,.04f),gold,.02f);
            star.RotationDegrees=new(0,0,45);
        }
        _stars.Position=new(0,StandingStarHeight,0);_stars.Visible=false;
        _nameLabel=new Label3D {Text=name,Font=BlastCircuit.UI.ArcadeTheme.Display,Position=new(0,1.87f,0),FontSize=84,PixelSize=.003f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("fff7d5"),OutlineModulate=new Color("232942"),OutlineSize=30,NoDepthTest=true};_nameLabel.PixelSize*=Math.Min(1,660f/Math.Max(1,_nameLabel.Font.GetStringSize(name,HorizontalAlignment.Left,-1,84).X));AddChild(_nameLabel);
        var shadow=Cylinder(this,new(0,.018f,0),.34f,.008f,art.Toy(new Color(.13f,.1f,.08f,.22f)));shadow.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
    }
    private static float FaceHemisphereZ(float x,float headY)
    {
        float y=headY/HelmetHalfHeight;
        return HelmetForward+HelmetDepth*Mathf.Sqrt(Mathf.Max(.0001f,1-y*y-Mathf.Pow(x/.427f,2)));
    }
    private static ArrayMesh CreateCurvedFacePatch(float width,float height,float centerX,float centerY,float offset,float roundness=.47f,bool seatToShell=true)
    {
        // A distinct faceplate with moderate cheek curvature. Its rounded return
        // bridges back into the helmet, preserving the original front outline.
        const int rings=12,segments=64;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector2 Outline(float phi)
        {
            float corner=roundness*.5f,flat=.5f-corner;
            var radial=new Vector2(Mathf.Cos(phi),Mathf.Sin(phi));
            var square=radial/Mathf.Max(Mathf.Abs(radial.X),Mathf.Abs(radial.Y))*.5f;
            var inner=square.Clamp(Vector2.One*-flat,Vector2.One*flat);
            return (inner+(square-inner).Normalized()*corner)*2;
        }
        Vector3 Point(float radius,int segment,float depth,float shellBlend=0)
        {
            var outline=Outline(Mathf.Tau*segment/segments);
            float x=width*.5f*radius*outline.X,y=height*.5f*radius*outline.Y;
            float hemisphere=FaceHemisphereZ(centerX+x,centerY+y);
            // Lift the perimeter slightly while following the continuous spherical front.
            float faceRadiusSquared=Mathf.Pow((centerX+x)/.317f,2)+Mathf.Pow((centerY+y-FaceCenterY)/.227f,2);
            float front=hemisphere+depth+.03f*faceRadiusSquared;
            float rear=seatToShell?hemisphere-.005f:front;
            return new(x,y,Mathf.Lerp(front,rear,shellBlend));
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,bool front)
        {
            var normal=(b-a).Cross(c-a);
            // Godot uses clockwise front faces. Caps are oriented by the hemisphere;
            // the lip is ordered explicitly around its outward perimeter.
            if(front&&normal.Z>0||!front&&normal.Z<0){surface.AddVertex(a);surface.AddVertex(c);surface.AddVertex(b);}
            else {surface.AddVertex(a);surface.AddVertex(b);surface.AddVertex(c);}
        }
        void Cap(float maxRadius,float depth,bool front,float shellBlend=0)
        {
            for(int ring=0;ring<rings;ring++)for(int segment=0;segment<segments;segment++)
            {
                float inner=maxRadius*ring/rings,outer=maxRadius*(ring+1)/rings;
                var a=Point(inner,segment,depth,shellBlend);var b=Point(outer,segment,depth,shellBlend);var c=Point(outer,segment+1,depth,shellBlend);var d=Point(inner,segment+1,depth,shellBlend);
                Triangle(a,b,c,front);if(ring>0)Triangle(a,c,d,front);
            }
        }
        Cap(.985f,offset,true);
        // Three contour rings make a short rounded return, nested in the curved rim.
        var profiles=new[]{(Radius:.985f,Depth:offset,ShellBlend:0f),(Radius:1f,Depth:offset-.003f,ShellBlend:0f),(Radius:.985f,Depth:offset-.012f,ShellBlend:1f)};
        for(int ring=0;ring<profiles.Length-1;ring++)for(int segment=0;segment<segments;segment++)
        {
            var a=Point(profiles[ring].Radius,segment,profiles[ring].Depth,profiles[ring].ShellBlend);var b=Point(profiles[ring+1].Radius,segment,profiles[ring+1].Depth,profiles[ring+1].ShellBlend);
            var c=Point(profiles[ring+1].Radius,segment+1,profiles[ring+1].Depth,profiles[ring+1].ShellBlend);var d=Point(profiles[ring].Radius,segment+1,profiles[ring].Depth,profiles[ring].ShellBlend);
            // Clockwise outward orientation around the closed return.
            surface.AddVertex(a);surface.AddVertex(c);surface.AddVertex(b);
            surface.AddVertex(a);surface.AddVertex(d);surface.AddVertex(c);
        }
        Cap(.985f,offset-.012f,false,1);
        // Generate the authored normals before deduplicating complete vertices.
        surface.GenerateNormals();surface.Index();return surface.Commit();
    }
    private static ArrayMesh CreateHelmet(bool extendedBack=true)
    {
        // An uninterrupted near-sphere makes every head angle softly round.
        // Its slight forward center balances the face without a heavy rear cranium.
        const int rings=24,segments=40;
        // The torso keeps its established pear proportions; only the helmet is retuned.
        float halfHeight=extendedBack?HelmetHalfHeight:.395f,taper=extendedBack?0:.075f;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int ring,int segment)
        {
            float theta=Mathf.Pi*ring/rings,phi=Mathf.Tau*segment/segments;
            float y=Mathf.Cos(theta),radius=Mathf.Sin(theta)*(1+taper*y);
            float z=(extendedBack?HelmetForward:0)+(extendedBack?HelmetDepth:.365f)*radius*Mathf.Sin(phi);
            return new(.427f*radius*Mathf.Cos(phi),halfHeight*y,z);
        }
        void Vertex(Vector3 p)
        {
            float normalZ=extendedBack?(p.Z-HelmetForward)/(HelmetDepth*HelmetDepth):p.Z/(.365f*.365f);
            surface.SetNormal(new Vector3(p.X/(.427f*.427f),p.Y/(halfHeight*halfHeight)-2*taper,normalZ).Normalized());
            surface.SetUV(new(.5f+Mathf.Atan2(p.Z,p.X)/Mathf.Tau,.5f-p.Y/(halfHeight*2)));
            surface.AddVertex(p);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            if((b-a).Cross(c-a).Dot(a+b+c)>0){Vertex(a);Vertex(c);Vertex(b);}
            else {Vertex(a);Vertex(b);Vertex(c);}
        }
        for(int ring=0;ring<rings;ring++)for(int segment=0;segment<segments;segment++)
        {
            var a=Point(ring,segment);var b=Point(ring+1,segment);
            var c=Point(ring+1,segment+1);var d=Point(ring,segment+1);
            if(ring<rings-1)Triangle(a,b,c);
            if(ring>0)Triangle(a,c,d);
        }
        surface.Index();return surface.Commit();
    }
    public void Pose(bool carrying,bool stunned,bool ghost,bool liftingPlayer=false)
    {
        if(_carrying&&!carrying)_throwPulse=1;
        _carrying=carrying;_stunned=stunned;_liftingPlayer=carrying&&liftingPlayer;
        if(!carrying)_charge=0;
        // A fresh ghost rises out of the blast instead of popping in at full strength.
        if(_ghost!=ghost){_ghostFade=ghost?1:0;SetGhostOpacity(_body,ghost?GhostTransparency(1):0);}
        _ghost=ghost;
    }
    private static void SetGhostOpacity(Node node,float transparency)
    {
        if(node is GeometryInstance3D geometry)geometry.Transparency=transparency;
        foreach(var child in node.GetChildren())SetGhostOpacity(child,transparency);
    }
    public void Animate(float dt,float distance)
    {
        _time+=dt;
        // Distance drives the gait; stopping never advances the feet in place.
        _walkPhase=Mathf.PosMod(_walkPhase+Mathf.Clamp(distance,0,.3f)/StrideLength,1);
        float previousSpeed=_speed;
        _speed=Mathf.Lerp(_speed,Mathf.Min(1,distance/Mathf.Max(dt,.001f)/3),1-Mathf.Exp(-10*dt));
        float acceleration=Mathf.Clamp((_speed-previousSpeed)/Mathf.Max(dt,.001f),-3,3);
        // ArenaView owns facing; its turn rate banks the body and leads with the head.
        float yawRate=_yawKnown&&dt>0?Mathf.Clamp((float)Mathf.AngleDifference(_lastYaw,Rotation.Y)/dt,-12,12):0;
        _lastYaw=Rotation.Y;_yawKnown=true;
        _carryBlend=Mathf.MoveToward(_carryBlend,_carrying?1:0,dt*6);
        _pumpPulse=Mathf.MoveToward(_pumpPulse,0,dt*3.6f);_throwPulse=Mathf.MoveToward(_throwPulse,0,dt*4.5f);
        _dropPulse=Mathf.MoveToward(_dropPulse,0,dt/.18f);
        float drop=Mathf.Sin(_dropPulse*Mathf.Pi);
        _kickWindupRemaining=Mathf.Max(0,_kickWindupRemaining-dt);
        _kickPulse=Mathf.MoveToward(_kickPulse,0,dt/.22f);_liftPulse=Mathf.MoveToward(_liftPulse,0,dt*3.2f);
        _deathPulse=Mathf.MoveToward(_deathPulse,0,dt*1.8f);_victoryPulse=Mathf.MoveToward(_victoryPulse,0,dt*.7f);
        _impactPulse=Mathf.MoveToward(_impactPulse,0,dt/.28f);_recoverPulse=Mathf.MoveToward(_recoverPulse,0,dt/.45f);
        float pump=Mathf.Sin(_pumpPulse*Mathf.Pi),toss=Mathf.Sin(_throwPulse*Mathf.Pi);
        float kickTime=1-_kickPulse;
        float kick=_kickWinding?-.5f*(1-Mathf.Clamp(_kickWindupRemaining/(4f/30f),0,1)):
            _kickPulse<=0?0:1-Mathf.SmoothStep(0,1,kickTime);
        float lift=Mathf.Sin(_liftPulse*Mathf.Pi),death=Mathf.Sin(_deathPulse*Mathf.Pi),victory=Mathf.Sin(_victoryPulse*Mathf.Pi);
        var leftStep=FootStride(_walkPhase);var rightStep=FootStride(Mathf.PosMod(_walkPhase+.5f,1));
        float walk=(rightStep.Reach-leftStep.Reach)*.5f*_speed;
        float idle=(1-_speed)*(1-_carryBlend)*(1-death);
        float breath=Mathf.Sin(_time*2.35f)*idle;
        float sway=Mathf.Sin(_time*1.2f)*idle;
        _downBlend=Mathf.MoveToward(_downBlend,(_stunned||_heldVictim||_airborne)&&!_ghost?1:0,dt*8);
        float action=Mathf.Max(Mathf.Abs(kick)*2,Mathf.Max(drop,Mathf.Max(toss,Mathf.Max(lift,Mathf.Max(death,Mathf.Max(victory,pump))))));
        float freeStride=_speed*(1-_carryBlend*.45f)*(1-_downBlend)*(1-Mathf.Clamp(action,0,1));
        // A bouncy scamper: squash just after each heel strike, rise through
        // push-off into a brief airborne beat, then drop into the next contact.
        float bounce=Bounce(Mathf.PosMod(_walkPhase*2,1)),low=(1-bounce)*.5f;
        // Ballistic weight: squaring the contact weight keeps the body near the
        // top of its hop for most of each beat, then drops it through a brief,
        // crisp landing, so the squash reads as a plop instead of a slow dip.
        low*=low;
        float bodyHop=(1-low)*.04f*freeStride;
        float suitSquash=(low*low*.055f-(1-low)*(1-low)*.018f)*freeStride;
        float hipWeight=-Mathf.Sin((_walkPhase-.06f)*Mathf.Tau)*freeStride;
        float leanTarget=_speed*RunLean*(1-_downBlend);
        _lean.Step(leanTarget+acceleration*.035f*(1-_downBlend),9,.42f,dt);
        _bank.Step(Mathf.Clamp(-yawRate*.022f*_speed,-.16f,.16f),10,.5f,dt);
        // Each glove pumps forward with the opposite boot. Springs give the swing
        // weight: it trails the legs slightly and overshoots when the actor stops.
        _leftArmSwing.Step(-leftStep.Reach*freeStride,30,.42f,dt);
        _rightArmSwing.Step(-rightStep.Reach*freeStride,30,.42f,dt);
        float swingLeft=_leftArmSwing.Value,swingRight=_rightArmSwing.Value;
        // Rigid short legs swing from fixed hips and stay under the leaning body.
        // A passing boot tucks up into the suit rather than stretching its stem.
        float legLean=-leanTarget*.85f;
        float leftAngle=-leftStep.Reach*LegSwing*_speed+legLean,rightAngle=(-rightStep.Reach*LegSwing*_speed+legLean)*(1-Mathf.Max(kick,0));
        _leftLeg.Rotation=new(leftAngle,0,0);_rightLeg.Rotation=new(rightAngle-kick*1.45f,0,kick*.12f);
        _leftLeg.Position=new(-.15f,.37f+leftStep.Tuck*TuckHeight*freeStride,leftStep.Reach*HipSlide*_speed);
        _rightLeg.Position=new(.15f,.37f+rightStep.Tuck*TuckHeight*freeStride,rightStep.Reach*HipSlide*_speed*(1-Mathf.Max(kick,0)));
        _leftBoot.Rotation=new(-leftAngle-_lean.Value+leftStep.Pitch*freeStride,_leftBoot.Rotation.Y,0);
        _rightBoot.Rotation=new(-rightAngle-_lean.Value+rightStep.Pitch*freeStride,_rightBoot.Rotation.Y,0);
        float leftImpact=leftStep.Impact*freeStride,rightImpact=rightStep.Impact*freeStride;
        _leftBoot.Scale=BootScale*new Vector3(1+leftImpact*.09f,1-leftImpact*.14f,1+leftImpact*.05f);
        _rightBoot.Scale=BootScale*new Vector3(1+rightImpact*.09f,1-rightImpact*.14f,1+rightImpact*.05f);
        float spread=.25f+_charge*.065f*_carryBlend+(_liftingPlayer?.05f*_carryBlend:0);
        _leftArm.Position=new(-spread,.68f+_carryBlend*.12f+Mathf.Max(0,swingLeft)*.015f,swingLeft*.02f);_rightArm.Position=new(spread,.68f+_carryBlend*.12f+Mathf.Max(0,swingRight)*.015f,swingRight*.02f);
        _leftArm.Scale=new(1,1+_carryBlend*.8f,1);_rightArm.Scale=new(1,1+_carryBlend*.8f,1);
        // Forward swings reach higher and cross in toward the chest; back swings stay short.
        float leftReach=-.8f*swingLeft-.25f*swingLeft*Mathf.Abs(swingLeft),rightReach=-.8f*swingRight-.25f*swingRight*Mathf.Abs(swingRight);
        float left=Mathf.Lerp(-.1f+leftReach+breath*.025f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        float right=Mathf.Lerp(-.14f+rightReach-breath*.018f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        _leftArm.Rotation=new(left-kick*.35f,-.07f-swingLeft*.1f,Mathf.Lerp(-.48f+breath*.025f+swingLeft*swingLeft*.14f,-.12f-_charge*.035f,_carryBlend));
        _rightArm.Rotation=new(right+kick*.4f-drop*.45f,.07f+swingRight*.1f,Mathf.Lerp(.50f-breath*.025f-swingRight*swingRight*.14f,.12f+_charge*.035f,_carryBlend));
        float shoulderTurn=(swingLeft-swingRight)*.042f*(1-_carryBlend*.85f);
        // Squash compresses toward the planted boots rather than pushing them through the floor.
        _body.Position=new(sway*.009f+hipWeight*.024f,bodyHop+suitSquash*.15f+breath*.009f-pump*.08f+lift*.04f+victory*.08f-death*.18f+(_ghost?.18f+Mathf.Sin(_time*3)*.08f:0),0);
        _body.Scale=new(1+pump*.06f+suitSquash*.5f,1-pump*.09f-death*.18f-suitSquash,1+pump*.06f+suitSquash*.5f);
        _body.Rotation=new(_lean.Value+low*.03f*freeStride-_carryBlend*_charge*.025f+toss*.12f,-shoulderTurn*.35f,(_stunned?Mathf.Sin(_time*9)*.22f:0)+death*.48f-hipWeight*.05f+walk*.012f+sway*.012f+_bank.Value);
        _body.Position+=new Vector3(0,-drop*.035f,kick*.045f+drop*.018f);
        _body.Rotation+=new Vector3(-kick*.16f,kick*.1f,0);
        ApplyMishapBody(dt);
        _upperBody.Rotation=new(low*.03f*freeStride,shoulderTurn,hipWeight*.022f);
        // The heavy helmet trails the body's rise and fall, nods into each landing,
        // counters the hip roll and leads turns. It keeps its round proportions.
        _headBob.Step(bodyHop,20,.32f,dt);
        float headLag=Mathf.Clamp(_headBob.Value-bodyHop,-.03f,.03f);
        _headRoll.Step(hipWeight*.06f-_bank.Value*.5f,12,.4f,dt);
        _headTurn.Step(Mathf.Clamp(yawRate*.035f,-.35f,.35f),14,.6f,dt);
        float glance=Mathf.Sin(_time*.73f)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(_time*.31f)),6)*idle;
        _head.Position=new(-hipWeight*.008f,HeadCenter+headLag*.6f,0);
        _head.Scale=new(1/(1+suitSquash*.5f),1/(1-suitSquash),1/(1+suitSquash*.5f));
        _head.Rotation=new(breath*.025f-_lean.Value*.55f-pump*.08f-headLag*2.2f,glance*.11f-shoulderTurn*.62f+_headTurn.Value,_headRoll.Value-sway*.025f);
        ApplyMishapHead();
        // The antenna ball trails the helmet: starts tip it back, steps make it
        // jiggle, and turns swing it outward before it settles upright again.
        float headPitch=_body.Rotation.X+_head.Rotation.X,headRoll=_body.Rotation.Z+_head.Rotation.Z;
        _antennaPitch.Step(headPitch-acceleration*.07f,13,.16f,dt);_antennaRoll.Step(headRoll,13,.16f,dt);
        float headHeight=_body.Position.Y+headLag*.6f;
        _antennaBob.Step(headHeight,18,.15f,dt);
        _antenna.Rotation=new(SoftLimit((_antennaPitch.Value-headPitch)*2.6f,.5f),0,SoftLimit((_antennaRoll.Value-headRoll)*2.6f,.5f));
        float jiggle=Mathf.Clamp(_antennaBob.Value-headHeight,-.03f,.03f);
        _antennaBall.Position=new(0,.116f+jiggle*.7f,0);
        _antennaBall.Scale=new Vector3(.225f,.225f,.225f)*new Vector3(1-jiggle*1.5f,1+jiggle*3,1-jiggle*1.5f);
        // Short asynchronous-looking blinks keep the roster alive without hiding the face in combat.
        float blinkPhase=_time%4.7f;
        float eyeHeight=blinkPhase>4.52f?Mathf.Max(.12f,Mathf.Abs(blinkPhase-4.61f)/.09f):1;
        foreach(var eye in _eyes)eye.Scale=new(1,eyeHeight,1);
        ApplyMishapFace();
        _nameLabel.Position=new(0,1.87f+_carryBlend*(.65f+_charge*.35f),0);
        Scale=Vector3.One*(_ghost?.85f:1.12f*StatusScale);
        if(_ghost&&_ghostFade>0){_ghostFade=Mathf.MoveToward(_ghostFade,0,dt/1.6f);SetGhostOpacity(_body,GhostTransparency(_ghostFade));}
    }
    /// <summary>Eases toward a limit instead of stopping dead against it.</summary>
    private static float SoftLimit(float value,float limit)=>limit*(float)Math.Tanh(value/limit);
    private static float Bounce(float beat)
    {
        // -1 just after heel strike, +1 late in push-off: a slightly slower rise than fall.
        float t=Mathf.PosMod(beat-.16f,1);
        return t<.56f?-Mathf.Cos(Mathf.Pi*t/.56f):Mathf.Cos(Mathf.Pi*(t-.56f)/.44f);
    }
    private static (float Reach,float Pitch,float Tuck,float Impact) FootStride(float phase)
    {
        // Heel strikes toe-up, grips flat while sweeping back at a nearly steady
        // ground speed, then rolls off the toe. The recovering boot follows through
        // behind, tucks up and curls its toe through passing, and whips forward
        // already moving back so contact matches the floor without a velocity kink.
        const float steady=.6f;
        if(phase<Plant)
        {
            float contact=phase/Plant;
            return ((1-steady)*Mathf.Cos(contact*Mathf.Pi)+steady*(1-2*contact),Mathf.Lerp(-.2f,.34f,Mathf.SmoothStep(0,1,contact)),0,contact<.3f?Mathf.Sin(contact/.3f*Mathf.Pi):0);
        }
        float swing=(phase-Plant)/(1-Plant),arc=Mathf.Pow(Mathf.Sin(swing*Mathf.Pi),2);
        float tangent=-2*steady*(1-Plant)/Plant,s2=swing*swing,s3=s2*swing;
        float reach=-(2*s3-3*s2+1)+(s3-2*s2+swing)*tangent+(-2*s3+3*s2)+(s3-s2)*tangent;
        return (reach,Mathf.Lerp(.34f,-.2f,Mathf.SmoothStep(0,1,swing))-arc*.12f,arc,0);
    }
    private struct Spring
    {
        public float Value,Velocity;
        /// <summary>Damped spring toward a target; fixed substeps keep 30/60/120 FPS alike.</summary>
        public void Step(float target,float frequency,float damping,float dt)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt*240));float h=dt/steps;
            for(int i=0;i<steps;i++)
            {
                Velocity+=(frequency*frequency*(target-Value)-2*damping*frequency*Velocity)*h;
                Value+=Velocity*h;
            }
        }
    }
}
