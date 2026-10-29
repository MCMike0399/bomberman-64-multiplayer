using Godot;
using Bomber.Game.Matches;
namespace BlastCircuit.InputDevices;

/// <summary>Engine input adapter. Every device produces the same authority-owned commands.</summary>
public static class PlayerControls
{
    public static PlayerInput Read(int seat,int humanCount=2)=>ControllerHub.Current.Read(seat);
    internal static PlayerInput Keyboard(int seat)
    {
        if(seat==0)return new(Axis(Key.A,Key.D),Axis(Key.W,Key.S),
            Input.IsPhysicalKeyPressed(Key.J)||Input.IsMouseButtonPressed(MouseButton.Left),
            Input.IsPhysicalKeyPressed(Key.K)||Input.IsMouseButtonPressed(MouseButton.Right),
            Down(Key.I,Key.R),Input.IsPhysicalKeyPressed(Key.L)||(OS.GetName()!="Windows"&&Input.IsPhysicalKeyPressed(Key.Shift)),Input.IsPhysicalKeyPressed(Key.O));
        if(seat==1)return new(Axis(Key.Left,Key.Right),Axis(Key.Up,Key.Down),
            Down(Key.N,Key.Enter,Key.Kp0),Down(Key.M,Key.Kp1),Down(Key.Comma,Key.Backspace,Key.Kp2),Input.IsPhysicalKeyPressed(Key.Ctrl),Down(Key.Period,Key.Kp3));
        return default;
    }
    internal static bool BombKey(int seat,Key key)=>seat==0?key is Key.J:key is Key.N or Key.Enter or Key.Kp0;
    internal static bool BombMouseButton(MouseButton button)=>button==MouseButton.Left;
    internal static bool RemoteKey(int seat,Key key)=>seat==0?key is Key.I or Key.R:key is Key.Comma or Key.Backspace or Key.Kp2;
    internal static bool StopKey(int seat,Key key)=>seat==0?key is Key.O:key is Key.Period or Key.Kp3;
    private static bool Down(Key first,Key second,Key third=Key.None)=>
        Input.IsPhysicalKeyPressed(first)||Input.IsPhysicalKeyPressed(second)||(third!=Key.None&&Input.IsPhysicalKeyPressed(third));
    private static float Axis(Key negative,Key positive)=>(Input.IsPhysicalKeyPressed(positive)?1:0)-(Input.IsPhysicalKeyPressed(negative)?1:0);
}
