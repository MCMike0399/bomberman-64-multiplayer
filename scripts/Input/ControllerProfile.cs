using Godot;
namespace BlastCircuit.InputDevices;

public enum ControllerFamily { Keyboard, Xbox, Nintendo, PlayStation, Generic }

/// <summary>Labels describe canonical physical positions, never swap gameplay actions by brand.</summary>
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
    public static string Actions(ControllerFamily family,int keyboardSeat=0)
    {
        if(family==ControllerFamily.Keyboard)return keyboardSeat==0?"J / SPACE  BOMB   K / SHIFT  CARRY   L / R  REMOTE":"N / ENTER  BOMB   M / CTRL  CARRY   , / BACKSPACE  REMOTE";
        var p=Labels(family);return $"{p.South}  BOMB   {p.West} / {p.Shoulder}  CARRY   {p.East} / R shoulder  REMOTE";
    }
    public static string Menu(ControllerFamily family)
    {
        if(family==ControllerFamily.Keyboard)return "ARROWS  SELECT   ENTER  CONFIRM   ESC  BACK   F11  SCREEN";
        var p=Labels(family);return $"STICK / D-PAD  SELECT   {p.South}  CONFIRM   {p.East}  BACK   F11  SCREEN";
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
