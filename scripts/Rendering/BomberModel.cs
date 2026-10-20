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
    private float _time,_speed,_carryBlend,_pumpPulse,_throwPulse;
    private int _charge;
    public int PumpAnimationCount {get;private set;}
    public float CarryBlend=>_carryBlend;
    public float PumpIntensity=>Mathf.Sin(_pumpPulse*Mathf.Pi);
    public Vector3 BombSocket=>_body.ToGlobal(new Vector3(0,Mathf.Lerp(.25f,1.47f,_carryBlend)+Mathf.Sin(_pumpPulse*Mathf.Pi)*.09f,.08f));
    public void Pump(int charge){_charge=charge;_pumpPulse=1;PumpAnimationCount++;}
    public BomberModel(ArtMaterials art,Color color,string name)
    {
        var suit=art.Color(color,.4f);var white=art.Color(new("fff7e9"),.36f);var pink=art.Color(new("ee4a90"),.38f);
        var face=art.Color(new("ffcb91"),.65f);var dark=art.Color(new("272341"),.5f);var gold=art.Color(new("ffd15a"),.36f);
        AddChild(_body);
        Sphere(_body,new(0,.63f,0),new(.48f,.57f,.4f),suit);
        Box(_body,new(0,.43f,.015f),new(.48f,.105f,.4f),dark,.035f);
        Box(_body,new(0,.44f,.23f),new(.14f,.13f,.055f),gold,.02f);
        // Rounded helmet and inset peach face make each player readable at game-camera distance.
        Box(_body,new(0,1.04f,0),new(.74f,.65f,.61f),suit,.16f);
        Box(_body,new(0,1.025f,.296f),new(.53f,.39f,.065f),dark,.075f);
        Box(_body,new(0,1.03f,.334f),new(.47f,.325f,.045f),face,.055f);
        Box(_body,new(-.115f,1.055f,.362f),new(.047f,.19f,.022f),dark,.016f);
        Box(_body,new(.115f,1.055f,.362f),new(.047f,.19f,.022f),dark,.016f);
        Cylinder(_body,new(0,1.38f,0),.065f,.12f,dark);
        Sphere(_body,new(0,1.49f,0),new(.22f,.22f,.22f),pink);
        _leftArm.Position=new(-.31f,.72f,0);_rightArm.Position=new(.31f,.72f,0);
        _body.AddChild(_leftArm);_body.AddChild(_rightArm);
        foreach(var arm in new[]{_leftArm,_rightArm})
        {
            Sphere(arm,new(0,-.12f,0),new(.16f,.29f,.16f),white);
            Sphere(arm,new(0,-.3f,.025f),new(.25f,.23f,.25f),pink);
        }
        _leftLeg.Position=new(-.15f,.37f,0);_rightLeg.Position=new(.15f,.37f,0);_body.AddChild(_leftLeg);_body.AddChild(_rightLeg);
        foreach(var leg in new[]{_leftLeg,_rightLeg})
        {
            Sphere(leg,new(0,-.13f,0),new(.17f,.28f,.17f),white);
            Sphere(leg,new(0,-.27f,.08f),new(.28f,.19f,.39f),pink);
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
        _time+=dt;_speed=Mathf.Lerp(_speed,Mathf.Min(1,distance/Mathf.Max(dt,.001f)/3),.2f);
        _carryBlend=Mathf.MoveToward(_carryBlend,_carrying?1:0,dt*6);
        _pumpPulse=Mathf.MoveToward(_pumpPulse,0,dt*3.6f);_throwPulse=Mathf.MoveToward(_throwPulse,0,dt*4.5f);
        float pump=Mathf.Sin(_pumpPulse*Mathf.Pi),toss=Mathf.Sin(_throwPulse*Mathf.Pi);
        float walk=Mathf.Sin(_time*13)*_speed;
        _leftLeg.Rotation=new(walk*.65f,0,0);_rightLeg.Rotation=new(-walk*.65f,0,0);
        float spread=.31f+_charge*.065f*_carryBlend;
        _leftArm.Position=new(-spread,.72f+_carryBlend*.12f,0);_rightArm.Position=new(spread,.72f+_carryBlend*.12f,0);
        _leftArm.Scale=_rightArm.Scale=new(1,1+_carryBlend*.8f,1);
        float left=Mathf.Lerp(-walk*.55f,-2.9f-pump*.18f,_carryBlend)+toss*.9f;
        float right=Mathf.Lerp(walk*.55f,-2.9f-pump*.18f,_carryBlend)+toss*.9f;
        _leftArm.Rotation=new(left,0,Mathf.Lerp(.12f,-.12f-_charge*.035f,_carryBlend));
        _rightArm.Rotation=new(right,0,Mathf.Lerp(-.12f,.12f+_charge*.035f,_carryBlend));
        _body.Position=new(0,Mathf.Abs(walk)*.045f-pump*.08f+(_ghost?.18f+Mathf.Sin(_time*3)*.08f:0),0);
        _body.Scale=new(1+pump*.06f,1-pump*.09f,1+pump*.06f);
        _body.Rotation=new(-_carryBlend*_charge*.025f+toss*.12f,0,_stunned?Mathf.Sin(_time*9)*.22f:0);
        _stars.Rotation=new(0,_time*3,0);
        _nameLabel.Position=new(0,1.87f+_carryBlend*(.65f+_charge*.35f),0);
        Scale=Vector3.One*(_ghost?.85f:1.12f);
    }
}
