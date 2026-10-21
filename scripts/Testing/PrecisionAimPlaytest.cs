using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using BlastCircuit;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
using System.Text.Json;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private static float AimNumber(JsonElement value,string key)=>value.GetProperty(key).GetSingle();
    private ThrowPreviewState PrecisionPreview=>_root.ThrowPreviews.Single();
    private async Task PrecisionCarrySetup()
    {
        await HandlingPractice();await GrappleWalk(0,6,18);
        // This entire row is deliberately clear in the stock stage. No fixture or actor writes.
        Input(0,new PlayerInput(1,0));await Frames(2);
        Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
        Input(0,new PlayerInput(0,0,B:true));await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","ordinary carry plus create lifts a bomb on the stock clear courtyard");
    }
    private async Task PrecisionAim()
    {
        Event("precision-aim-policy",new{stage="sunken-temple",ordinaryInput=true,stateMutation=false,camera="normal battle",preview="shared static terrain landing; moving contacts may interrupt"});
        await PrecisionCarrySetup();
        float x=AimNumber(Player,"x"),z=AimNumber(Player,"z");
        Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(4);
        var initial=PrecisionPreview;
        Check(initial.Aiming&&initial.AimSet&&initial.End=="Landing"&&initial.LandingMarkerVisible,"explicit aim establishes a visible deliberate landing on clear floor");
        Check(_root.ThrowPreviews.Count==1&&initial.PlayerId==Player.GetProperty("id").GetString(),"guide belongs only to the controlled local carrier");
        await Capture("precision-aim-initial",0);
        float headingX=AimNumber(Player,"aimX"),headingZ=AimNumber(Player,"aimZ");
        Input(0,new PlayerInput(1,0,B:true,Aim:true));
        // Observe the first accepted nudge, rather than assuming two render
        // frames always contain two fresh 15 Hz state snapshots.
        await WaitState(_=>Math.Abs(AimNumber(Player,"aimX")-headingX)+Math.Abs(AimNumber(Player,"aimZ")-headingZ)>.005f,
            "first deliberate nudge arrives in an authoritative snapshot",6);
        float nudge=MathF.Acos(Math.Clamp(headingX*AimNumber(Player,"aimX")+headingZ*AimNumber(Player,"aimZ"),-1,1));
        Input(0,new PlayerInput(1,0,B:true,Aim:true));await Frames(6);
        float held=MathF.Acos(Math.Clamp(headingX*AimNumber(Player,"aimX")+headingZ*AimNumber(Player,"aimZ"),-1,1));
        Event("precision-aim-nudge",new{nudge,held});
        Check(nudge>.005f&&held>nudge*2&&held<1.2f,"short direction nudge makes a finer turn than holding the same bounded aim input");
        Check(Math.Abs(AimNumber(Player,"x")-x)<.02f&&Math.Abs(AimNumber(Player,"z")-z)<.02f,"turning the landing direction plants the real carrier");
        Input(0,new PlayerInput(-1,0,B:true,Aim:true));await Frames(8);
        Input(0,new PlayerInput(0,1,B:true,Aim:true));
        await WaitState(_=>AimNumber(Player,"aimRange")<initial.AimRange-1,
            "nearer range adjustment reaches the authority",40);
        Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(4);
        var near=PrecisionPreview;
        Check(near.AimRange<initial.AimRange-.8f&&near.End=="Landing","nearer aim moves the selected landing inward on clear stock floor");
        await Capture("precision-aim-near",0);
        Input(0,new PlayerInput(0,-1,B:true,Aim:true));
        await WaitState(_=>AimNumber(Player,"aimRange")>near.AimRange+2,
            "farther range adjustment reaches the authority",50);
        Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(4);
        var far=PrecisionPreview;
        Event("precision-aim-range",new{near,far});
        Check(far.AimRange>near.AimRange+1.9f&&far.End=="Landing"&&far.X>near.X+1.4f,"held farther input continuously extends the actual landing guide");
        await Capture("precision-aim-far",0);
        for(int pump=0;pump<3;pump++)
        {
            Input(0,new PlayerInput(0,0,A:true,B:true,Aim:true));await Frames(2);
            Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(2);
        }
        var pumped=PrecisionPreview;
        Check(pumped.Pumps==3&&Math.Abs(pumped.X-far.X)<.03f&&Math.Abs(pumped.Z-far.Z)<.03f&&Math.Abs(pumped.Y-far.Y)<.03f,"three real pump presses enlarge power while preserving the deliberately selected landing");
        await Capture("precision-aim-pumped",0);
        float aimX=AimNumber(Player,"aimX"),aimZ=AimNumber(Player,"aimZ");
        Input(0,new PlayerInput(-1,0,B:true));await Frames(4);
        Input(0,new PlayerInput(0,0,B:true));await Frames(6);
        var retained=PrecisionPreview;
        Check(!retained.Aiming&&retained.AimSet&&AimNumber(Player,"x")<x-.3f,"releasing aim restores ordinary carrier movement and keeps deliberate intent");
        Check(Math.Abs(AimNumber(Player,"aimX")-aimX)<.001f&&Math.Abs(AimNumber(Player,"aimZ")-aimZ)<.001f&&Math.Abs(retained.AimRange-pumped.AimRange)<.001f,"walking after aim release preserves selected throw direction and range");
        Check(Math.Abs((retained.X-pumped.X)-(AimNumber(Player,"x")-x))<.06f&&retained.End=="Landing","retained landing guide follows the moved carrier without resetting direction");
        await Capture("precision-aim-retained",0);
        Input(0,default);await Frames(4);
        Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null&&HandlingBomb.GetProperty("state").GetInt32()==3,"releasing carry commits the deliberate high lob");
        Check(_root.ThrowPreviews.Count==0,"local guide disappears immediately after the actual release");
        await Capture("precision-aim-flight",0);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==1&&HandlingBomb.GetProperty("state").GetInt32()==0,"deliberate stock courtyard lob reaches resting floor",100);
        Check(Math.Abs(AimNumber(HandlingBomb,"x")-retained.X)<.12f&&Math.Abs(AimNumber(HandlingBomb,"z")-retained.Z)<.12f&&Math.Abs(AimNumber(HandlingBomb,"y")-retained.Y)<.12f,"actual bomb landing matches the displayed static-floor preview in X Z and height");
        Check(!Player.GetProperty("stunned").GetBoolean(),"deliberate landing clears its stationary carrier without immunity");
        Event("precision-aim-landing",new{initial,near,far,pumped,retained,actual=HandlingBomb});
        await Capture("precision-aim-landing",0);
        Input(0,default);_root.Back();await Frames(3);
    }

    private async Task AimInputs()=>await WithControllerFocusRetries(AimInputsAttempt,"aim-inputs");
    private async Task AimInputsAttempt()
    {
        var hub=ControllerHub.Current;float? keyboardRange=null,keyboardTurn=null;
        Event("aim-input-mode",new{source="ordinary synthetic keyboard/canonical events; Switch uses production native bridge",physicalInputClaim=false,triggerThreshold=.35f});
        try
        {
            foreach(var pad in new GamepadDevice?[]{null,QaPads[0],QaPads[1],QaPads[2]})
            {
                CtlReleaseAll();hub.OverrideDevicesForPlaytest(pad is null?[]:[pad]);
                // Navigation uses ordinary authority movement, then native controls perform
                // their own create/carry actions on a fresh empty-handed player.
                await HandlingPractice();await GrappleWalk(0,6,18);
                Input(0,new PlayerInput(1,0));await Frames(2);Input(0,default);await Frames(2);
                _root.InjectInput(0,null);await ControllerFocusReady();_controllerFocusTrial=true;
                string name=pad?.Name??"Keyboard";
                if(pad is null)
                {
                    foreach(var key in new[]{Key.Ctrl,Key.Enter,Key.Backspace})
                    {
                        CtlKey(key,true);
                        Check(hub.ActiveKeyboardSeat==1&&hub.ActiveFamily==ControllerFamily.Keyboard,"P2 "+key+" selects the second keyboard's actual control prompts");
                        CtlKey(key,false);
                    }
                }
                float[] nativeAxes=new float[6];
                void Axis(JoyAxis axis,float value)
                {
                    if(pad is null)return;
                    if(pad.RuntimeId>=1000)
                    {
                        nativeAxes[(int)axis]=value;
                        hub.NativeAxesForPlaytest(pad.RuntimeId,nativeAxes);
                    }
                    else CtlAxis(pad.RuntimeId,axis,value);
                }
                if(pad is {RuntimeId:>=1000})
                {
                    float[] snapshot=[.15f,-.25f,.35f,-.45f,.55f,.65f];
                    hub.NativeAxesForPlaytest(pad.RuntimeId,snapshot);
                    for(int axis=0;axis<6;axis++)
                        Check(Math.Abs(Godot.Input.GetJoyAxis(pad.RuntimeId,(JoyAxis)axis)-snapshot[axis])<.0001f,
                            "Native production sampler routes canonical axis "+axis+" from a backend snapshot");
                    hub.NativeAxesForPlaytest(pad.RuntimeId,nativeAxes);
                    Event("native-axis-snapshot-test",new{source="synthetic backend values through production sampling/publication",physicalInputClaim=false,axes=6});
                }
                JoyButton primary=pad is null?JoyButton.A:ControllerProfile.BombButton(ControllerProfile.Family(pad.Name));
                JoyButton carry=pad is null?JoyButton.X:ControllerProfile.CarryButton(ControllerProfile.Family(pad.Name));
                void Carry(bool down){if(pad is null)CtlKey(Key.Space,down);else CtlButton(pad.RuntimeId,carry,down);}
                void Primary(bool down){if(pad is null)CtlKey(Key.J,down);else CtlButton(pad.RuntimeId,primary,down);}
                void Aim(float value){if(pad is null)CtlKey(Key.Shift,value>.35f);else Axis(JoyAxis.TriggerRight,value);}
                void Range(bool down){if(pad is null)CtlKey(Key.W,down);else Axis(JoyAxis.LeftY,down?-1:0);}
                void Turn(bool down){if(pad is null)CtlKey(Key.D,down);else Axis(JoyAxis.LeftX,down?1:0);}
                Carry(true);await ControllerFrames(2);Primary(true);await ControllerFrames(2);Primary(false);await ControllerFrames(2);
                Check(Player.GetProperty("carrying").GetString()=="bomb",name+": native carry plus primary creates the held bomb");
                if(pad is not null){Aim(.3f);await ControllerFrames(2);Check(!Player.GetProperty("aiming").GetBoolean(),name+": below-threshold right trigger does not enter aim");}
                Aim(1);await ControllerFrames(4);float startRange=AimNumber(Player,"aimRange"),px=AimNumber(Player,"x"),pz=AimNumber(Player,"z");
                Check(Player.GetProperty("aiming").GetBoolean(),name+": Shift or canonical right trigger enters the authoritative aim stance");
                Range(true);await ControllerFrames(8);Range(false);await ControllerFrames(2);
                float delta=AimNumber(Player,"aimRange")-startRange;
                Check(delta>.2f&&delta<BombThrowRules.AimRangeSpeed*8/30f,name+": native range starts with fine nudges below the maximum sweep rate");
                if(keyboardRange is {} expectedRange)Check(Math.Abs(delta-expectedRange)<.035f,name+": native range adjustment matches the keyboard through the shared ramp");
                else keyboardRange=delta;
                float ax=AimNumber(Player,"aimX"),az=AimNumber(Player,"aimZ");
                Turn(true);await ControllerFrames(8);Turn(false);await ControllerFrames(2);
                float turn=MathF.Acos(Math.Clamp(ax*AimNumber(Player,"aimX")+az*AimNumber(Player,"aimZ"),-1,1));
                Check(turn>.15f&&turn<BombThrowRules.AimTurnSpeed*8/30f,name+": native turn starts with fine nudges below the maximum angular rate");
                Check(ax*AimNumber(Player,"aimZ")-az*AimNumber(Player,"aimX")>.05f,
                    name+": right input turns aim clockwise on the stage");
                if(keyboardTurn is {} expectedTurn)Check(Math.Abs(turn-expectedTurn)<.025f,name+": native turn adjustment matches keyboard through the shared ramp");
                else keyboardTurn=turn;
                Check(Math.Abs(AimNumber(Player,"x")-px)<.02f&&Math.Abs(AimNumber(Player,"z")-pz)<.02f,name+": native aim directions do not move the carrier");
                Primary(true);await ControllerFrames(2);Primary(false);await ControllerFrames(2);
                Check(HandlingBomb.GetProperty("pumps").GetInt32()==1&&Math.Abs(AimNumber(Player,"aimRange")-(startRange+delta))<.01f,name+": native primary pumps while preserving selected range");
                await Capture("precision-input-"+(pad?.RuntimeId.ToString()??"keyboard"),0);
                Aim(0);Carry(false);await ControllerFrames(4);
                Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null,name+": native carry release throws after aim");
                Event("aim-native-result",new{name,delta,turn,physicalInputClaim=false});
                _controllerFocusTrial=false;CtlReleaseAll();_root.Back();await Frames(3);
            }
        }
        finally
        {
            _controllerFocusTrial=false;CtlReleaseAll();if(_root.ActiveSession!=null)_root.Back();hub.RestorePhysicalDevicesForPlaytest();await Frames(3);
        }
    }
}
