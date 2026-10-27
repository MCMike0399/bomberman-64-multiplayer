using Godot;
namespace BlastCircuit.Arena.Models;

/// <summary>
/// A presentation-only stand-in for a blasted player: a white-hot zap, a sooty panicked
/// flail with a sizzling antenna fuse, then a stiff pose that crumbles into the smoke puff.
/// After Bomberman 64's battle deaths, where the burning victim thrashes before vanishing in smoke.
/// </summary>
public partial class BomberModel
{
    public const float BurnoutFlailEnd=1.55f,BurnoutCrumbleEnd=1.76f;
    private bool _charred;
    public bool Charred=>_charred;
    /// <summary>World anchors flames cling to: helmet crown, belly, both gloves and both boots.</summary>
    public Vector3 BurnAnchor(int index)=>(index%6) switch
    {
        0=>_head.ToGlobal(new Vector3(0,.2f,0)),
        1=>_upperParts.ToGlobal(new Vector3(0,.53f,.1f)),
        2=>_leftArm.ToGlobal(new Vector3(0,-.22f,.05f)),
        3=>_rightArm.ToGlobal(new Vector3(0,-.22f,.05f)),
        4=>_leftBoot.GlobalPosition,
        _=>_rightBoot.GlobalPosition
    };
    public void BeginBurnout()
    {
        ShowName(false);
        foreach(var eye in _spiralEyes)eye.Visible=false;
        foreach(var eye in _squintEyes)eye.Visible=false;
        _stars.Visible=false;
        Paint(true);
    }
    private void Paint(bool flash)
    {
        _charred=!flash;
        var hot=_art.Color(new Color("fff1b8"),.5f,true);
        // Matte brown char, deliberately unlike the glossy black suit.
        var soot=_art.Color(new Color("43352d"),.97f);var ash=_art.Color(new Color("6a5548"),.97f);
        var eyes=_art.Color(new Color("fffaf0"),.4f,true);var fuse=_art.Color(new Color("ffb13b"),.3f,true);
        void Visit(Node node)
        {
            if(node is MeshInstance3D mesh)
                mesh.MaterialOverride=flash?hot:_eyes.Contains(mesh)?eyes:mesh==_antennaBall?fuse:mesh.Name=="FaceSurface"?ash:soot;
            foreach(var child in node.GetChildren())Visit(child);
        }
        Visit(_body);
    }
    /// <summary>Poses the stand-in at <paramref name="age"/> seconds after the blast; the effect owns root travel.</summary>
    public void AnimateBurnout(float age)
    {
        if(age>.09f&&!_charred)Paint(false);
        float flail=Mathf.Clamp((age-.08f)/.12f,0,1)*(1-Mathf.SmoothStep(BurnoutFlailEnd-.12f,BurnoutFlailEnd,age));
        float stiff=Mathf.SmoothStep(BurnoutFlailEnd-.12f,BurnoutFlailEnd,age);
        float crumble=Mathf.SmoothStep(BurnoutFlailEnd+.04f,BurnoutCrumbleEnd,age);
        float zap=1-Mathf.Clamp(age/.12f,0,1);
        // Running in place on hot feet while both gloves windmill.
        float run=Mathf.Sin(age*24);
        _leftLeg.Rotation=new(run*.95f*flail,0,-.1f*flail-.18f*stiff);
        _rightLeg.Rotation=new(-run*.95f*flail,0,.1f*flail+.18f*stiff);
        _leftLeg.Position=new(-.15f,.37f,0);_rightLeg.Position=new(.15f,.37f,0);
        _leftBoot.Rotation=new(-run*.45f*flail,_leftBoot.Rotation.Y,0);_rightBoot.Rotation=new(run*.45f*flail,_rightBoot.Rotation.Y,0);
        float windmill=Mathf.PosMod(-age*17,Mathf.Tau);
        _leftArm.Rotation=new(Mathf.LerpAngle(windmill,-2.95f,stiff),0,-.75f*flail-.2f*stiff);
        _rightArm.Rotation=new(Mathf.LerpAngle(windmill+Mathf.Pi,-2.95f,stiff),0,.75f*flail+.2f*stiff);
        _leftArm.Position=new(-.25f,.68f,0);_rightArm.Position=new(.25f,.68f,0);
        _leftArm.Scale=_rightArm.Scale=Vector3.One;
        // The zap stretches the body tall; then it thrashes and finally crumbles into an ash heap.
        _body.Position=new(0,-crumble*.12f,0);
        _body.Rotation=new(-.18f*flail,Mathf.Sin(age*6)*.3f*flail,Mathf.Sin(age*9)*.22f*flail);
        _body.Scale=new Vector3(1-zap*.12f+crumble*.4f,1+zap*.25f-crumble*.78f,1-zap*.12f+crumble*.4f);
        _upperBody.Rotation=Vector3.Zero;
        _head.Position=new(0,HeadCenter,0);_head.Scale=Vector3.One;
        _head.Rotation=new(-.15f*flail,Mathf.Sin(age*23)*.45f*flail,0);
        // The antenna becomes a sizzling fuse that whips with the panic.
        _antenna.Rotation=new(Mathf.Sin(age*31)*.4f*flail,0,Mathf.Sin(age*27+1)*.35f*flail);
        _antennaBall.Scale=Vector3.One*.225f*(1+Mathf.Sin(age*47)*.18f*flail);
        _antennaBall.Position=new(0,.116f,0);
        // Wide white eyes on the sooty face, with two shocked blinks.
        float blink=Mathf.Abs(age-1.05f)<.05f||Mathf.Abs(age-BurnoutFlailEnd-.04f)<.05f?.12f:1;
        foreach(var eye in _eyes){eye.Visible=true;eye.Scale=new(1.45f,1.12f*blink,1);}
    }
}
