using Godot;
namespace BlastCircuit.InputDevices;

public enum ControllerFamily { Keyboard, Xbox, Nintendo, PlayStation, Generic }

/// <summary>Canonical positions provide device labels; action choices follow each controller family.</summary>
public static class ControllerProfile
{
    public static ControllerFamily Family(string name,int vendor=0)
    {
        var n=name.ToLowerInvariant();
        if(vendor==0x057e||n.Contains("switch")||n.Contains("nintendo"))return ControllerFamily.Nintendo;
        if(vendor==0x054c||n.Contains("dualsense")||n.Contains("dualshock")||n.Contains("ps5")||n.Contains("playstation"))return ControllerFamily.PlayStation;
        if(vendor==0x045e||n.Contains("xbox")||n.Contains("xinput"))return ControllerFamily.Xbox;
        return ControllerFamily.Generic;
    }
    public static (string South,string West,string East,string Shoulder,string Start) Labels(ControllerFamily family)=>family switch
    {
        ControllerFamily.Nintendo=>("B","Y","A","L","+"),
        ControllerFamily.PlayStation=>("Cross","Square","Circle","L1","Options"),
        ControllerFamily.Xbox=>("A","X","B","LB","Menu"),
        _=>("South","West","East","L shoulder","Start")
    };
    public static JoyButton BombButton(ControllerFamily family)=>family==ControllerFamily.Nintendo?JoyButton.B:JoyButton.A;
    public static JoyButton CarryButton(ControllerFamily family)=>family==ControllerFamily.Nintendo?JoyButton.A:JoyButton.X;
    public static JoyButton RemoteButton(ControllerFamily family)=>family==ControllerFamily.Nintendo?JoyButton.Y:JoyButton.B;
    public static JoyButton ConfirmButton(ControllerFamily family)=>BombButton(family);
    public static JoyButton CancelButton(ControllerFamily family)=>family==ControllerFamily.Nintendo?JoyButton.A:JoyButton.B;
    public static string Actions(ControllerFamily family,int keyboardSeat=0)
    {
        if(family==ControllerFamily.Keyboard)return keyboardSeat==0?"J  BOMB   SPACE  CARRY   K  AIM   L  REMOTE":"N / ENTER  BOMB   M / NUM 1  CARRY   CTRL  AIM   , / BACKSPACE  REMOTE";
        return $"{ButtonName(family,BombButton(family))}  BOMB   {ButtonName(family,CarryButton(family))} / {ButtonName(family,JoyButton.LeftShoulder)}  CARRY   {AimName(family)}  AIM   {ButtonName(family,RemoteButton(family))} / {ButtonName(family,JoyButton.RightShoulder)}  REMOTE";
    }
    public static string AimName(ControllerFamily family)=>family switch
    {ControllerFamily.Nintendo=>"ZR",ControllerFamily.PlayStation=>"R2",ControllerFamily.Xbox=>"RT",ControllerFamily.Keyboard=>"K",_=>"R trigger"};
    public static string Menu(ControllerFamily family)
    {
        if(family==ControllerFamily.Keyboard)return "WASD / ARROWS  SELECT   ENTER  CONFIRM   ESC  BACK   F11  SCREEN";
        return $"STICK / D-PAD  SELECT   {ButtonName(family,ConfirmButton(family))}  CONFIRM   {ButtonName(family,CancelButton(family))}  BACK   F11  SCREEN";
    }
    public static string ButtonName(ControllerFamily family,JoyButton button)
    {
        var p=Labels(family);
        return button switch
        {
            JoyButton.A=>p.South,JoyButton.B=>p.East,JoyButton.X=>p.West,
            JoyButton.Y=>family==ControllerFamily.PlayStation?"Triangle":family==ControllerFamily.Nintendo?"X":"Y",
            JoyButton.LeftShoulder=>p.Shoulder,JoyButton.RightShoulder=>family==ControllerFamily.PlayStation?"R1":family==ControllerFamily.Nintendo?"R":"RB",
            JoyButton.Start=>p.Start,_=>button.ToString()
        };
    }
}
