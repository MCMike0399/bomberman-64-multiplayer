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
    private float _time,_walkPhase,_speed,_carryBlend,_pumpPulse,_throwPulse,_kickPulse,_liftPulse,_deathPulse,_victoryPulse;
    private int _charge;
    private readonly ArtMaterials _art;
    private readonly List<MeshInstance3D> _suitParts=[],_torsoParts=[];
    private readonly Node3D _head=new();
    private static ArrayMesh? _helmetMesh,_torsoMesh;
    private readonly List<MeshInstance3D> _eyes=[];
    private Color _suitColor;
    public int PumpAnimationCount {get;private set;}
    public float WalkPose=>_leftLeg.Rotation.X;
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
            case "kick":_kickPulse=1;break;
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
        _head.Position=new(0,1.10f,0);_body.AddChild(_head);
        var helmet=new MeshInstance3D {Name="HelmetShell",Mesh=_helmetMesh??=CreateHelmet(),MaterialOverride=suit};
        _head.AddChild(helmet);_suitParts.Add(helmet);
        RoundedBox(_head,new(0,-.025f,.247f),new(.646f,.466f,.016f),art.Color(new("8b755c"),.8f),.48f);
        RoundedBox(_head,new(0,-.025f,.263f),new(.634f,.454f,.024f),face,.47f);
        foreach(float x in new[]{-.134f,.134f})
            _eyes.Add(RoundedBox(_head,new(x,-.005f,.287f),new(.036f,.193f,.012f),dark,.8f));
        Cylinder(_head,new(0,.397f,0),.058f,.1f,dark);
        Sphere(_head,new(0,.481f,0),new(.225f,.225f,.225f),pink);
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
        }
        AddChild(_stars);
        for(int i=0;i<3;i++)
        {
            float a=Mathf.Tau*i/3;var star=Box(_stars,new(Mathf.Cos(a)*.42f,1.62f,Mathf.Sin(a)*.42f),new(.13f,.13f,.04f),gold,.02f);
            star.RotationDegrees=new(0,0,45);
        }
        _stars.Visible=false;
        _nameLabel=new Label3D {Text=name,Position=new(0,1.87f,0),FontSize=30,PixelSize=.009f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("fff7d5"),OutlineModulate=new Color("232942"),OutlineSize=10,NoDepthTest=true};AddChild(_nameLabel);
        var shadow=Cylinder(this,new(0,.018f,0),.34f,.008f,art.Color(new Color(.13f,.1f,.08f,.22f)));shadow.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
    }
    private static ArrayMesh CreateHelmet(bool flattenFront=true)
    {
        // A real egg silhouette, fuller at the crown and tucked under the cheeks.
        // This avoids the long planar sides of a rounded cuboid at oblique battle angles.
        const int rings=24,segments=40;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(int ring,int segment)
        {
            float theta=Mathf.Pi*ring/rings,phi=Mathf.Tau*segment/segments;
            float y=Mathf.Cos(theta),radius=Mathf.Sin(theta)*(1+.075f*y);
            float z=.365f*radius*Mathf.Sin(phi);
            // Seat only the central face cap. Flattening the entire front hemisphere
            // also squared the crown and chin when seen from the side.
            if(flattenFront&&z>.24f)z=.24f+.02f*(1-Mathf.Exp(-(z-.24f)/.02f));
            else if(flattenFront&&z<0)z*=.435f/.365f;
            return new(.427f*radius*Mathf.Cos(phi),.395f*y,z);
        }
        void Vertex(Vector3 p)
        {
            float normalZ=p.Z/(.365f*.365f);
            if(flattenFront&&p.Z>.24f)
            {
                // Transform the original egg normal through the eased front profile.
                // This keeps the cheek lighting continuous instead of a sharp flat-cap seam.
                float remaining=Mathf.Max(1-(p.Z-.24f)/.02f,.0001f);
                float originalZ=.24f-.02f*Mathf.Log(remaining);
                float slope=remaining;
                normalZ=originalZ/(.365f*.365f*Mathf.Max(slope,.015f));
            }
            else if(flattenFront&&p.Z<0)normalZ=p.Z/(.435f*.435f);
            surface.SetNormal(new Vector3(p.X/(.427f*.427f),p.Y/(.395f*.395f)-.15f,normalZ).Normalized());
            surface.SetUV(new(.5f+Mathf.Atan2(p.Z,p.X)/Mathf.Tau,.5f-p.Y/.79f));
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
        _time+=dt;_walkPhase+=Mathf.Min(distance,.3f)*4.8f;
        float previousSpeed=_speed;
        _speed=Mathf.Lerp(_speed,Mathf.Min(1,distance/Mathf.Max(dt,.001f)/3),1-Mathf.Exp(-10*dt));
        float acceleration=Mathf.Clamp((_speed-previousSpeed)/Mathf.Max(dt,.001f),-3,3);
        _carryBlend=Mathf.MoveToward(_carryBlend,_carrying?1:0,dt*6);
        _pumpPulse=Mathf.MoveToward(_pumpPulse,0,dt*3.6f);_throwPulse=Mathf.MoveToward(_throwPulse,0,dt*4.5f);
        _kickPulse=Mathf.MoveToward(_kickPulse,0,dt*3.8f);_liftPulse=Mathf.MoveToward(_liftPulse,0,dt*3.2f);
        _deathPulse=Mathf.MoveToward(_deathPulse,0,dt*1.8f);_victoryPulse=Mathf.MoveToward(_victoryPulse,0,dt*.7f);
        float pump=Mathf.Sin(_pumpPulse*Mathf.Pi),toss=Mathf.Sin(_throwPulse*Mathf.Pi);
        float kick=Mathf.Sin(_kickPulse*Mathf.Pi),lift=Mathf.Sin(_liftPulse*Mathf.Pi),death=Mathf.Sin(_deathPulse*Mathf.Pi),victory=Mathf.Sin(_victoryPulse*Mathf.Pi);
        float walk=Mathf.Sin(_walkPhase)*_speed;
        float idle=(1-_speed)*(1-_carryBlend)*(1-death);
        float breath=Mathf.Sin(_time*2.35f)*idle;
        float sway=Mathf.Sin(_time*1.2f)*idle;
        float stepBob=(1-Mathf.Cos(_walkPhase*2))*.023f*_speed;
        _leftLeg.Rotation=new(walk*.65f,0,0);_rightLeg.Rotation=new(-walk*.65f-kick*1.15f,0,kick*.12f);
        float spread=.265f+_charge*.065f*_carryBlend;
        _leftArm.Position=new(-spread,.68f+_carryBlend*.12f,0);_rightArm.Position=new(spread,.68f+_carryBlend*.12f,0);
        _leftArm.Scale=_rightArm.Scale=new(1,1+_carryBlend*.8f,1);
        float left=Mathf.Lerp(-.1f-walk*.55f+breath*.025f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        float right=Mathf.Lerp(-.14f+walk*.55f-breath*.018f,-2.9f-pump*.18f,_carryBlend)-toss*1.7f-lift*.2f-victory*2.7f;
        _leftArm.Rotation=new(left,-.07f,Mathf.Lerp(-.48f+breath*.025f,-.12f-_charge*.035f,_carryBlend));
        _rightArm.Rotation=new(right,.07f,Mathf.Lerp(.50f-breath*.025f,.12f+_charge*.035f,_carryBlend));
        _body.Position=new(sway*.009f,stepBob+breath*.009f-pump*.08f+lift*.04f+victory*.08f-death*.18f+(_ghost?.18f+Mathf.Sin(_time*3)*.08f:0),0);
        _body.Scale=new(1+pump*.06f,1-pump*.09f-death*.18f,1+pump*.06f);
        _body.Rotation=new(_speed*.055f+acceleration*.012f-_carryBlend*_charge*.025f+toss*.12f,-walk*.025f,(_stunned?Mathf.Sin(_time*9)*.22f:0)+death*.48f+walk*.042f+sway*.012f);
        // Counter the suit's step roll so the big head floats with a soft, attentive lag.
        float glance=Mathf.Sin(_time*.73f)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(_time*.31f)),6)*idle;
        _head.Position=new(0,1.10f-stepBob*.25f,0);
        _head.Rotation=new(breath*.025f-_speed*.035f-pump*.08f,glance*.11f+walk*.02f,-walk*.055f-sway*.025f);
        // Short asynchronous-looking blinks keep the roster alive without hiding the face in combat.
        float blinkPhase=_time%4.7f;
        float eyeHeight=blinkPhase>4.52f?Mathf.Max(.12f,Mathf.Abs(blinkPhase-4.61f)/.09f):1;
        foreach(var eye in _eyes)eye.Scale=new(1,eyeHeight,1);
        _stars.Rotation=new(0,_time*3,0);
        _nameLabel.Position=new(0,1.87f+_carryBlend*(.65f+_charge*.35f),0);
        Scale=Vector3.One*(_ghost?.85f:1.12f);
    }
}
