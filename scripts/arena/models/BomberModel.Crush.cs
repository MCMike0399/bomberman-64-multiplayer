using Godot;
namespace BlastCircuit.Arena.Models;

/// <summary>Wall-death stand-in; uses the held mishap's squeezed eyes and short fixed limbs.</summary>
public partial class BomberModel
{
    public void AnimateCrush(float age)
    {
        ShowName(false);_stars.Visible=false;
        foreach(var eye in _eyes)eye.Visible=false;
        foreach(var eye in _spiralEyes)eye.Visible=false;
        foreach(var eye in _squintEyes)eye.Visible=true;
        float struggle=Mathf.Sin(age*24)*(1-Mathf.SmoothStep(.6f,1.1f,age));
        _leftArm.Rotation=new(-.4f+struggle*.35f,0,-1.15f);
        _rightArm.Rotation=new(-.4f-struggle*.35f,0,1.15f);
        _leftLeg.Rotation=new(struggle*.3f,0,-.24f);
        _rightLeg.Rotation=new(-struggle*.3f,0,.24f);
        _head.Rotation=new(0,0,struggle*.08f);
        _antenna.Rotation=new(struggle*.3f,0,Mathf.Sin(age*29)*.24f*Mathf.Exp(-age*2));
    }
}
