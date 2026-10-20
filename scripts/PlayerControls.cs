using Godot;
using Bomber.Game;
using Bomber.Client;
namespace BlastCircuit;

/// <summary>Keyboard seats and connected gamepads produce the same authority-owned commands.</summary>
public static class PlayerControls
{
    public static PlayerInput Read(int seat,int humanCount=2)
    {
        float x = 0, z = 0; bool a = false, b = false, r = false;
        if (seat == 0)
        {
            x = Axis(Key.A, Key.D); z = Axis(Key.W, Key.S);
            a = Input.IsPhysicalKeyPressed(Key.Space); b = Input.IsPhysicalKeyPressed(Key.Shift); r = Input.IsPhysicalKeyPressed(Key.R);
        }
        if (seat == 1)
        {
            x = Axis(Key.Left, Key.Right); z = Axis(Key.Up, Key.Down);
            a = Input.IsPhysicalKeyPressed(Key.Enter); b = Input.IsPhysicalKeyPressed(Key.Ctrl); r = Input.IsPhysicalKeyPressed(Key.Backspace);
        }
        var pads = Input.GetConnectedJoypads();
        if (LocalControllerLayout.GamepadIndex(seat,humanCount,pads.Count) is { } padIndex)
        {
            int pad = pads[padIndex];
            float px = Input.GetJoyAxis(pad, JoyAxis.LeftX), pz = Input.GetJoyAxis(pad, JoyAxis.LeftY);
            if (new Vector2(px, pz).Length() > .2f) { x = px; z = pz; }
            a |= Input.IsJoyButtonPressed(pad, JoyButton.A); b |= Input.IsJoyButtonPressed(pad, JoyButton.X); r |= Input.IsJoyButtonPressed(pad, JoyButton.B);
        }
        return new PlayerInput(x, z, a, b, r);
    }
    private static float Axis(Key negative, Key positive) => (Input.IsPhysicalKeyPressed(positive) ? 1 : 0) - (Input.IsPhysicalKeyPressed(negative) ? 1 : 0);
}
