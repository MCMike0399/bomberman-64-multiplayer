using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Rendering;

/// <summary>Articulated original mesh character, animated from authoritative movement and action state.</summary>
public partial class BomberModel : Node3D
{
    private readonly Node3D _body=new(),_leftArm=new(),_rightArm=new(),_leftLeg=new(),_rightLeg=new();
    private Node3D _stars=new();
    private Label3D _nameLabel=null!;
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
    private float _leftFootLift,_rightFootLift;
    private Vector3 _headStrideLag;
    private const float HeadCenter=1.085f,HelmetHalfHeight=.375f,HelmetDepth=.405f,HelmetForward=.045f,FaceCenterY=-.010f,EyeCenterY=.010f;
    private static ArrayMesh? _helmetMesh,_torsoMesh,_faceMesh,_faceRimMesh;
    private static readonly Dictionary<float,ArrayMesh> EyeMeshes=[];
    private readonly List<MeshInstance3D> _eyes=[];
    private Color _suitColor;
    public int PumpAnimationCount {get;private set;}
    public float WalkPose=>_leftLeg.Rotation.X;
    public float LeftFootLift=>(_leftLeg.Transform*_leftBoot.Position).Y-.145f;
    public float RightFootLift=>(_rightLeg.Transform*_rightBoot.Position).Y-.145f;
    private bool _kickWinding;
    private float _kickWindupRemaining;
    public float KickPose=>_kickWinding?-(1-Mathf.Clamp(_kickWindupRemaining/(4f/30f),0,1)):_kickPulse;
    public void KickWindup(float remaining){_kickWinding=remaining>0;_kickWindupRemaining=remaining;}
    public float CarryBlend=>_carryBlend;
    public float PumpIntensity=>Mathf.Sin(_pumpPulse*Mathf.Pi);
    public Vector3 BombSocket=>_body.ToGlobal(new Vector3(0,Mathf.Lerp(.25f,1.47f,_carryBlend)+Mathf.Sin(_pumpPulse*Mathf.Pi)*.09f,.08f));
    public void Pump(int charge){_charge=charge;_pumpPulse=1;PumpAnimationCount++;}
    public void ShowName(bool visible)=>_nameLabel.Visible=visible;
    public void SetSuitColor(Color color)
    {
        if(_suitColor==color)return;
        _suitColor=color;
        foreach(var part in _suitParts)part.MaterialOverride=_art.Color(color,.46f);
        foreach(var part in _torsoParts)part.MaterialOverride=_art.Color(TorsoColor(color),.46f);
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
            case "win":case "victory":_victoryPulse=1;break;
        }
    }
    public BomberModel(ArtMaterials art,Color color,string name)
    {
        _art=art;_suitColor=color;
        _time=(color.R*3.1f+color.G*5.3f+color.B*7.7f)%4.7f;
        var suit=art.Color(color,.46f);var white=art.Color(new("fff7e9"),.48f);var pink=art.Color(new("ee4a90"),.42f);
        var face=art.Color(new("ffe0b7"),.78f);var dark=art.Color(new("272341"),.55f);var gold=art.Color(new("ffd15a"),.36f);
        AddChild(_body);
        // The suit is a compact pear: tucked shoulders over a slightly fuller waist.
        var torso=new MeshInstance3D {Name="Torso",Mesh=_torsoMesh??=CreateHelmet(false),MaterialOverride=art.Color(TorsoColor(color),.46f),
            Position=new(0,.53f,0),Scale=new(.43f/.854f,.38f/.79f,.40f/.73f),Rotation=new(0,0,Mathf.Pi)};
        _body.AddChild(torso);_torsoParts.Add(torso);
        _torsoParts.Add(Sphere(_body,new(0,.722f,0),new(.17f,.065f,.17f),art.Color(TorsoColor(color),.46f)));
        RoundedBox(_body,new(0,.405f,.015f),new(.425f,.085f,.37f),dark);
        RoundedBox(_body,new(0,.41f,.21f),new(.13f,.12f,.055f),gold,.35f);
        RoundedBox(_body,new(0,.41f,.242f),new(.066f,.06f,.012f),dark,.2f);
        // A softly rounded helmet sits over a slimmer suit; the shallow face remains easy to read.
        _head.Position=new(0,HeadCenter,0);_body.AddChild(_head);
        var helmet=new MeshInstance3D {Name="HelmetShell",Mesh=_helmetMesh??=CreateHelmet(),MaterialOverride=suit};
        _head.AddChild(helmet);_suitParts.Add(helmet);
        _head.AddChild(new MeshInstance3D {Name="FaceRim",Mesh=_faceRimMesh??=CreateCurvedFacePatch(.646f,.466f,0,FaceCenterY,.009f),MaterialOverride=art.Color(new("8b755c"),.8f),Position=new(0,FaceCenterY,0)});
        _head.AddChild(new MeshInstance3D {Name="FaceSurface",Mesh=_faceMesh??=CreateCurvedFacePatch(.634f,.454f,0,FaceCenterY,.016f),MaterialOverride=face,Position=new(0,FaceCenterY,0)});
        foreach(float x in new[]{-.134f,.134f})
        {
            // Keep two simple eyes laid against the faceplate's moderate curvature.
            if(!EyeMeshes.TryGetValue(x,out var eyeMesh))EyeMeshes[x]=eyeMesh=CreateCurvedFacePatch(.036f,.193f,x,EyeCenterY,.022f,.8f,false);
            var eye=new MeshInstance3D {Mesh=eyeMesh,MaterialOverride=dark,Position=new(x,EyeCenterY,0)};
            _head.AddChild(eye);_eyes.Add(eye);
        }
        Cylinder(_head,new(0,HelmetHalfHeight+.002f,HelmetForward),.058f,.1f,dark);
        Sphere(_head,new(0,HelmetHalfHeight+.086f,HelmetForward),new(.225f,.225f,.225f),pink);
        _leftArm.Position=new(-.265f,.68f,0);_rightArm.Position=new(.265f,.68f,0);
        _body.AddChild(_leftArm);_body.AddChild(_rightArm);
        foreach(var arm in new[]{_leftArm,_rightArm})
        {
            Sphere(arm,new(0,-.073f,0),new(.13f,.19f,.13f),white);
            RoundedBox(arm,new(0,-.155f,.015f),new(.165f,.04f,.165f),pink,1);
            Sphere(arm,new(0,-.22f,.05f),new(.23f,.225f,.245f),pink);
            Sphere(arm,new(arm==_leftArm?.09f:-.09f,-.21f,.12f),new(.105f,.14f,.12f),pink);
        }
        _leftLeg.Position=new(-.16f,.37f,0);_rightLeg.Position=new(.16f,.37f,0);_body.AddChild(_leftLeg);_body.AddChild(_rightLeg);
        foreach(var leg in new[]{_leftLeg,_rightLeg})
        {
            Sphere(leg,new(0,-.075f,0),new(.15f,.19f,.15f),white);
            float outward=leg==_leftLeg?-.24f:.17f;
            var boot=Sphere(leg,new(outward*.11f,-.225f,leg==_leftLeg?.09f:.065f),new(.315f,.25f,.38f),pink);boot.Rotation=new(0,outward,0);
            if(leg==_leftLeg)_leftBoot=boot;else _rightBoot=boot;
        }
        AddChild(_stars);
        for(int i=0;i<3;i++)
        {
            float a=Mathf.Tau*i/3;var star=Box(_stars,new(Mathf.Cos(a)*.42f,1.62f,Mathf.Sin(a)*.42f),new(.13f,.13f,.04f),gold,.02f);
            star.RotationDegrees=new(0,0,45);
        }
        _stars.Visible=false;
        _nameLabel=new Label3D {Text=name,Font=BlastCircuit.UI.ArcadeTheme.Display,Position=new(0,1.87f,0),FontSize=28,PixelSize=.009f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("fff7d5"),OutlineModulate=new Color("232942"),OutlineSize=10,NoDepthTest=true};AddChild(_nameLabel);
        var shadow=Cylinder(this,new(0,.018f,0),.34f,.008f,art.Color(new Color(.13f,.1f,.08f,.22f)));shadow.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
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
        surface.GenerateNormals();return surface.Commit();
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
        return surface.Commit();
    }
    public void Pose(bool carrying,bool stunned,bool ghost)
    {
        if(_carrying&&!carrying)_throwPulse=1;
        _carrying=carrying;_stunned=stunned;_stars.Visible=stunned;
        if(!carrying)_charge=0;
        if(_ghost!=ghost)SetGhostOpacity(_body,ghost?.48f:0);
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
        // One complete left/right stride covers 1.9 units, independent of frame rate.
        // Distance drives the gait; stopping never advances the feet in place.
        _walkPhase=Mathf.PosMod(_walkPhase+Mathf.Clamp(distance,0,.3f)/1.9f,1);
        float previousSpeed=_speed;
        _speed=Mathf.Lerp(_speed,Mathf.Min(1,distance/Mathf.Max(dt,.001f)/3),1-Mathf.Exp(-10*dt));
        float acceleration=Mathf.Clamp((_speed-previousSpeed)/Mathf.Max(dt,.001f),-3,3);
        _carryBlend=Mathf.MoveToward(_carryBlend,_carrying?1:0,dt*6);
        _pumpPulse=Mathf.MoveToward(_pumpPulse,0,dt*3.6f);_throwPulse=Mathf.MoveToward(_throwPulse,0,dt*4.5f);
        _dropPulse=Mathf.MoveToward(_dropPulse,0,dt/.18f);
        float drop=Mathf.Sin(_dropPulse*Mathf.Pi);
        _kickWindupRemaining=Mathf.Max(0,_kickWindupRemaining-dt);
        _kickPulse=Mathf.MoveToward(_kickPulse,0,dt/.22f);_liftPulse=Mathf.MoveToward(_liftPulse,0,dt*3.2f);
        _deathPulse=Mathf.MoveToward(_deathPulse,0,dt*1.8f);_victoryPulse=Mathf.MoveToward(_victoryPulse,0,dt*.7f);
        float pump=Mathf.Sin(_pumpPulse*Mathf.Pi),toss=Mathf.Sin(_throwPulse*Mathf.Pi);
        float kickTime=1-_kickPulse;
        float kick=_kickWinding?-.5f*(1-Mathf.Clamp(_kickWindupRemaining/(4f/30f),0,1)):
            _kickPulse<=0?0:1-Mathf.SmoothStep(0,1,kickTime);
        float lift=Mathf.Sin(_liftPulse*Mathf.Pi),death=Mathf.Sin(_deathPulse*Mathf.Pi),victory=Mathf.Sin(_victoryPulse*Mathf.Pi);
        var leftStep=FootStride(_walkPhase);var rightStep=FootStride(Mathf.PosMod(_walkPhase+.5f,1));
        float walk=(rightStep.Reach-leftStep.Reach)*.5f*_speed;
        _leftFootLift=leftStep.Lift*_speed;_rightFootLift=rightStep.Lift*_speed;
        float idle=(1-_speed)*(1-_carryBlend)*(1-death);
        float breath=Mathf.Sin(_time*2.35f)*idle;
        float sway=Mathf.Sin(_time*1.2f)*idle;
        float stepBob=(_leftFootLift+_rightFootLift)*.32f;
        _downBlend=Mathf.MoveToward(_downBlend,(_stunned||_heldVictim||_airborne)&&!_ghost?1:0,dt*8);
        // Weight arrives over the supporting hip before the shoulders and helmet
        // catch up. This offset phrasing gives the short-legged walk some swagger.
        float freeStride=_speed*(1-_carryBlend*.78f)*(1-_downBlend)*(1-death)*(1-Mathf.Max(Mathf.Abs(kick),Mathf.Max(drop,toss)));
        float hipWeight=-Mathf.Sin((_walkPhase-.07f)*Mathf.Tau)*freeStride;
        float armSwing=Mathf.Sin((_walkPhase-.16f)*Mathf.Tau)*freeStride;
        float otherArmSwing=Mathf.Sin((_walkPhase-.13f)*Mathf.Tau)*freeStride;
        float contact=(.5f+.5f*Mathf.Cos(_walkPhase*Mathf.Tau*2))*freeStride;
        float spring=Mathf.Sin(_walkPhase*Mathf.Tau*2-.45f)*freeStride;
        float suitSquash=contact*.015f;
        _headStrideLag=_headStrideLag.Lerp(new Vector3(spring,armSwing,hipWeight),1-Mathf.Exp(-12*dt));
        float leftAngle=-leftStep.Reach*.63f*_speed,rightAngle=-rightStep.Reach*.63f*_speed*(1-Mathf.Max(kick,0));
        _leftLeg.Rotation=new(leftAngle,0,0);_rightLeg.Rotation=new(rightAngle-kick*1.45f,0,kick*.12f);
        // Compensate each boot's pivot arc so support stays low and only the
        // returning foot clears the floor. Boots retain their original shape/yaw.
        _leftLeg.Position=new(-.16f,.37f+FootPivotHeight(leftAngle,.09f)+_leftFootLift,leftStep.Reach*.13f*_speed);
        _rightLeg.Position=new(.16f,.37f+FootPivotHeight(rightAngle,.065f)+_rightFootLift,rightStep.Reach*.13f*_speed+kick*.12f);
        _leftBoot.Rotation=new(-leftAngle,_leftBoot.Rotation.Y,0);_rightBoot.Rotation=new(-rightAngle,_rightBoot.Rotation.Y,0);
        float spread=.265f+_charge*.065f*_carryBlend;
        _leftArm.Position=new(-spread,.68f+_carryBlend*.12f+Mathf.Max(0,-armSwing)*.028f,0);_rightArm.Position=new(spread,.68f+_carryBlend*.12f+Mathf.Max(0,otherArmSwing)*.022f,0);
        _leftArm.Scale=_rightArm.Scale=new(1,1+_carryBlend*.8f,1);
        float left=Mathf.Lerp(-.1f-armSwing*.86f+breath*.025f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        float right=Mathf.Lerp(-.14f+otherArmSwing*.76f-breath*.018f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        _leftArm.Rotation=new(left-kick*.35f,-.07f-armSwing*.08f,Mathf.Lerp(-.48f+breath*.025f-armSwing*.055f,-.12f-_charge*.035f,_carryBlend));
        _rightArm.Rotation=new(right+kick*.4f-drop*.45f,.07f-otherArmSwing*.07f,Mathf.Lerp(.50f-breath*.025f-otherArmSwing*.045f,.12f+_charge*.035f,_carryBlend));
        _body.Position=new(sway*.009f+hipWeight*.032f,stepBob+spring*.006f+breath*.009f-pump*.08f+lift*.04f+victory*.08f-death*.18f+(_ghost?.18f+Mathf.Sin(_time*3)*.08f:0),0);
        _body.Scale=new(1+pump*.06f+suitSquash*.5f,1-pump*.09f-death*.18f-suitSquash,1+pump*.06f+suitSquash*.5f);
        _body.Rotation=new(_speed*.065f+spring*.018f+acceleration*.012f-_carryBlend*_charge*.025f+toss*.12f,-armSwing*.07f,(_stunned?Mathf.Sin(_time*9)*.22f:0)+death*.48f-hipWeight*.075f+walk*.018f+sway*.012f);
        _body.Position+=new Vector3(0,-drop*.035f,kick*.045f+drop*.018f);
        _body.Rotation+=new Vector3(-kick*.16f,kick*.1f,0);
        _body.Position=_body.Position.Lerp(new Vector3(0,.22f,0),_downBlend);
        _body.Rotation=_body.Rotation.Lerp(new Vector3(_airborne?Mathf.Sin(_time*8)*.3f:0,0,1.35f+(_heldVictim?Mathf.Sin(_time*12)*.045f:0)),_downBlend);
        // Counter the suit's step roll so the big head floats with a soft, attentive lag.
        float glance=Mathf.Sin(_time*.73f)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(_time*.31f)),6)*idle;
        _head.Position=new(-_headStrideLag.Z*.014f,HeadCenter-stepBob*.4f-spring*.004f,0);
        // The suit compresses slightly at contact; the approved round helmet
        // keeps its proportions while its pose follows with a soft delayed nod.
        _head.Scale=new(1/(1+suitSquash*.5f),1/(1-suitSquash),1/(1+suitSquash*.5f));
        _head.Rotation=new(breath*.025f-_speed*.035f-pump*.08f-_headStrideLag.X*.016f,glance*.11f+_headStrideLag.Y*.035f,_headStrideLag.Z*.085f-sway*.025f);
        // Short asynchronous-looking blinks keep the roster alive without hiding the face in combat.
        float blinkPhase=_time%4.7f;
        float eyeHeight=blinkPhase>4.52f?Mathf.Max(.12f,Mathf.Abs(blinkPhase-4.61f)/.09f):1;
        foreach(var eye in _eyes)eye.Scale=new(1,eyeHeight,1);
        _stars.Rotation=new(0,_time*3,0);
        _nameLabel.Position=new(0,1.87f+_carryBlend*(.65f+_charge*.35f),0);
        Scale=Vector3.One*(_ghost?.85f:1.12f);
    }
    private static float FootPivotHeight(float angle,float forward)=>.225f*(Mathf.Cos(angle)-1)+forward*Mathf.Sin(angle);
    private static (float Reach,float Lift) FootStride(float phase)
    {
        // A longer grounded support and brief eased return give the step a
        // readable contact, rather than continuously pedalling both feet.
        const float support=.64f;
        if(phase<support)return (1-2*phase/support,0);
        float swing=(phase-support)/(1-support),arc=Mathf.Sin(swing*Mathf.Pi);
        return (Mathf.Lerp(-1,1,Mathf.SmoothStep(0,1,swing)),arc*arc*.045f);
    }
}
