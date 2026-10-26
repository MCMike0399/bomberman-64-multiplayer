using System.Numerics;
using System.Text.Json;
using Bomber.Client.Controls;
using Bomber.Game.Matches;

static class ControllerChecks
{
    public static void Run(Action<bool,string> check,Action<Action,string> throws)
    {
        static bool Near(float a,float b,float tolerance=.00001f)=>Math.Abs(a-b)<=tolerance;
        static float Speed(PlayerInput p)=>new Vector2(p.X,p.Z).Length();
        var menu=new MenuStickNavigation();
        check(menu.Step(0,.3f,.016)==0&&menu.Step(0,.44f,.016)==0&&menu.Step(0,.46f,.016)==4,"Menu stick activates on gradual press threshold crossing");
        check(menu.Step(0,.4f,.1)==0&&menu.Step(0,.35f,.1)==0,"Menu hysteresis does not retrigger around press threshold");
        check(menu.Step(0,.6f,.21)==4&&menu.Step(0,.6f,.13)==4,"Menu held direction repeats independently of motion report frequency");
        check(menu.Step(0,-.6f,.01)==3,"Menu reversal is immediate without neutral report");
        check(menu.Step(0,0,.01)==0&&menu.Step(0,-.6f,.01)==3,"Menu neutral release rearms direction");
        menu.Reset();check(menu.Step(.6f,.65f,0)==4&&menu.Step(.68f,.65f,.01)==0,"Near-diagonal menu noise does not switch navigation axes");
        check(menu.Step(float.NaN,0,0)==0&&menu.Step(0,.6f,0)==4,"Malformed menu motion resets safely");
        var cardinal=InputPolicy.Resolve(new PlayerInput(1,0));
        var diagonal=InputPolicy.Resolve(new PlayerInput(1,1));
        var analog=InputPolicy.Resolve(default,new PlayerInput(.3f,.4f));
        check(Near(Speed(cardinal),1)&&Near(Speed(diagonal),1)&&Near(Speed(analog),1),"Keyboard cardinal/diagonal and partial-stick directions share unit speed");
        check(Near(analog.X,.6f)&&Near(analog.Z,.8f),"Analog direction is preserved instead of eight-direction snapping");
        check(InputPolicy.Direction(.2f,0,.2f)==Vector2.Zero&&InputPolicy.Direction(.14f,.14f,.2f)==Vector2.Zero,"Radial deadzone rejects boundary and combined drift");
        check(Near(InputPolicy.Direction(.15f,.15f,.2f).Length(),1),"Direction outside radial deadzone activates at full speed");
        check(InputPolicy.Direction(float.NaN,1)==Vector2.Zero&&InputPolicy.Direction(1,float.PositiveInfinity)==Vector2.Zero,"Non-finite axes reject the whole direction");
        check(Near(InputPolicy.Direction(float.MaxValue,float.MaxValue).Length(),1),"Finite extreme axes do not overflow normalization");
        throws(()=>InputPolicy.Direction(1,0,-.1f),"Negative deadzones rejected");
        throws(()=>InputPolicy.Direction(1,0,1),"Deadzone one rejected");
        var dpad=InputPolicy.Resolve(new PlayerInput(1,0),new PlayerInput(0,1),new PlayerInput(-1,0));
        check(dpad.X==-1&&dpad.Z==0,"D-pad overrides stick and keyboard without adding movement");
        var pad=InputPolicy.Resolve(new PlayerInput(1,0),new PlayerInput(0,.8f));
        check(pad.X==0&&pad.Z==1,"Stick overrides keyboard without amplification");
        var fallback=InputPolicy.Resolve(new PlayerInput(1,0),new PlayerInput(.05f,.05f));
        check(fallback.X==1&&fallback.Z==0,"Drift cannot suppress keyboard movement");
        var buttons=InputPolicy.Resolve(new PlayerInput(1,0,A:true),new PlayerInput(0,1,B:true),new PlayerInput(0,-1,R:true));
        check(buttons.A&&buttons.B&&buttons.R&&Near(Speed(buttons),1),"Alternative buttons remain usable with one movement vector");
        var a=new GamepadDevice(11,"guid-a","Pad Alpha");var b=new GamepadDevice(12,"guid-b","Pad Beta");
        var bindings=new MatchControllerBindings(4,new[]{a,b});
        check(bindings.DeviceIdForSeat(0)==null&&bindings.DeviceIdForSeat(1)==null&&bindings.DeviceIdForSeat(2)==11&&bindings.DeviceIdForSeat(3)==12,"Initial layout keeps two keyboard seats then pads");
        bindings.Refresh(new[]{b});
        check(bindings.DeviceIdForSeat(2)==null&&bindings.DeviceIdForSeat(3)==12,"Unplugging first pad cannot shift the second player's seat");
        check(bindings.Seats[2].LastKnownDevice?.Name=="Pad Alpha"&&!bindings.Seats[2].IsConnected,"Disconnected seat retains prompt identity");
        var again=new GamepadDevice(29," GUID-A ","pad alpha");bindings.Refresh(new[]{b,again});
        check(bindings.DeviceIdForSeat(2)==29&&bindings.DeviceIdForSeat(3)==12,"New runtime ID reclaims canonical GUID/name seat");
        bindings.Refresh(new[]{again,b});
        check(bindings.DeviceIdForSeat(2)==29&&bindings.DeviceIdForSeat(3)==12,"Enumeration order cannot swap established assignments");
        bindings.Refresh(new[]{new GamepadDevice(29,"different-guid","Different Pad"),b});
        check(bindings.DeviceIdForSeat(2)==null&&bindings.DeviceIdForSeat(3)==12,"Reused runtime ID with different identity cannot hijack a reservation");
        var small=new MatchControllerBindings(2,new[]{a,b});small.Refresh(new[]{b});
        check(small.DeviceIdForSeat(0)==null&&small.DeviceIdForSeat(1)==12,"Optional pads stay stable in two-human keyboard layout");
        var later=new MatchControllerBindings(4,Array.Empty<GamepadDevice>());later.Refresh(new[]{a});later.Refresh(new[]{a,b});
        check(later.DeviceIdForSeat(2)==11&&later.DeviceIdForSeat(3)==12,"New pads fill unused seats without shifting others");
        var twins=new MatchControllerBindings(4,new[]{new GamepadDevice(40,"same-model","Twin Pad"),new GamepadDevice(41,"same-model","Twin Pad")});
        twins.Refresh(new[]{new GamepadDevice(41,"same-model","Twin Pad"),new GamepadDevice(50,"same-model","Twin Pad")});
        check(twins.DeviceIdForSeat(2)==50&&twins.DeviceIdForSeat(3)==41,"Identical pad reconnect is safe while sibling stays connected");
        twins.Refresh(Array.Empty<GamepadDevice>());twins.Refresh(new[]{new GamepadDevice(60,"same-model","Twin Pad"),new GamepadDevice(61,"same-model","Twin Pad")});
        check(twins.DeviceIdForSeat(2)==null&&twins.DeviceIdForSeat(3)==null&&twins.Seats[2].Ambiguous&&twins.Seats[3].Ambiguous,"Simultaneously replaced twins require confirmation");
        check(twins.BindDeviceToSeat(2,60)&&twins.DeviceIdForSeat(2)==60,"Explicit confirmation resolves ambiguous device");
        check(!twins.BindDeviceToSeat(3,60),"Explicit assignment cannot steal another human's pad");
        check(!twins.BindDeviceToSeat(0,61),"Large match keyboard seats cannot be overwritten by pads");
        throws(()=>new MatchControllerBindings(4,new[]{a,new GamepadDevice(11,"other","Other")}),"Duplicate runtime IDs rejected");
        throws(()=>bindings.DeviceIdForSeat(4),"Invalid human seat rejected");
        var unknown=new MatchControllerBindings(1,new[]{new GamepadDevice(70,"","")});
        unknown.Refresh(Array.Empty<GamepadDevice>());unknown.Refresh(new[]{new GamepadDevice(71,"","")});
        check(unknown.DeviceIdForSeat(0)==null&&unknown.BindDeviceToSeat(0,71),"Unidentified replacement requires explicit assignment");
        float card=Travel(InputPolicy.Resolve(new PlayerInput(1,0))),diag=Travel(InputPolicy.Resolve(new PlayerInput(1,1))),stick=Travel(InputPolicy.Resolve(default,new PlayerInput(.3f,.4f)));
        check(Math.Abs(card-1.5f)<.02f&&Math.Abs(diag-card)<.02f&&Math.Abs(stick-card)<.02f,"Actual LocalMatch travels equally far for keyboard cardinal/diagonal and partial stick");
        check(Travel(InputPolicy.Resolve(default,new PlayerInput(.1f,.1f)))==0,"Actual LocalMatch remains still under stick drift");
    }
    private static float Travel(PlayerInput input)
    {
        var match=new LocalMatch(new MatchOptions {HumanPlayers=1,BotPlayers=0,Practice=true,Stage="rock-garden"});
        for(int i=0;i<70;i++)match.Tick();
        static Vector2 Position(LocalMatch m)
        {
            using var state=JsonDocument.Parse(m.StateJson!);var p=state.RootElement.GetProperty("d").GetProperty("players")[0];return new(p.GetProperty("x").GetSingle(),p.GetProperty("z").GetSingle());
        }
        var before=Position(match);match.SetInput(0,input);for(int i=0;i<10;i++)match.Tick();return Vector2.Distance(before,Position(match));
    }
}
