using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.InputDevices;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PlacementQuality()
    {
        Event("placement-input-policy",new {placement="actual keyboard and Switch printed-A events",physicalButtonsTested=false,positioning="ordinary stock-temple movement",stateMutation=false});
        var hub=ControllerHub.Current;Camera3D? closeup=null;Camera3D? battleCamera=null;
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[1]]);
            await HandlingPractice();
            // Reproduce the fractional-cell placement failure using ordinary input,
            // then relinquish harness movement before sending real engine button events.
            for(int i=0;i<80&&Math.Abs(Player.GetProperty("x").GetSingle()-11.9f)>.015f;i++)
            {float dx=11.9f-Player.GetProperty("x").GetSingle();Input(0,new PlayerInput(Math.Clamp(dx*5,-1,1),0));await Frames(1);}
            Input(0,new PlayerInput(.12f,0));await Frames(1);Input(0,default);await Frames(2);_root.InjectInput(0,null);
            Check(Math.Abs(Player.GetProperty("x").GetSingle()-11.9f)<.05f,"ordinary movement reaches the previously rejected fractional flat-floor position");
            await ControllerFocusReady();await Frames(3);
            if(_scenario=="placement")
            {
                battleCamera=_root.GetViewport().GetCamera3D();closeup=new Camera3D {Fov=46,Near=.05f,Far=100};_root.AddChild(closeup);
                var target=TraversalModel(0).GlobalPosition+Vector3.Up*.65f;
                closeup.Position=target+new Vector3(0,5,10);closeup.LookAt(target);closeup.MakeCurrent();await Frames(6);
                Event("placement-inspection-camera",new {frame=Engine.GetProcessFrames(),purpose="actual actor and near-foot drop"});
            }
            float x=Player.GetProperty("x").GetSingle(),z=Player.GetProperty("z").GetSingle();int bombCue=Cue("bomb");
            CtlKey(Key.J,true);CtlKey(Key.J,false);await Frames(3);
            Check(State.GetProperty("bombs").GetArrayLength()==1&&Cue("bomb")==bombCue+1,"short keyboard primary edge places at fractional boundary with zero previous bombs");
            var first=State.GetProperty("bombs")[0];float bx=first.GetProperty("x").GetSingle(),bz=first.GetProperty("z").GetSingle();
            Check(Math.Abs(bx-x)<=.3f&&Math.Abs(bz-z)<=.3f,"accepted placement sits by the actual player's feet");
            Check(TraversalModel(0).PlacementPose>0,"accepted drop animates the actual character's placing hand");
            await Capture("placement-at-feet",0);
            CtlKey(Key.D,true);await Frames(10);CtlKey(Key.D,false);await Frames(3);
            Check(Player.GetProperty("x").GetSingle()>x+1.3f,"player keeps walking through first departure instead of getting pinned by their bomb");
            Check(State.GetProperty("bombs")[0].GetProperty("state").GetInt32()==0&&Math.Abs(State.GetProperty("bombs")[0].GetProperty("x").GetSingle()-bx)<.01f,"departing leaves the original bomb stationary behind");
            Check(!State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="kick-windup"),"departing does not secretly start a kick");
            await Capture("placement-left-behind",0);
            for(int i=0;i<2;i++)
            {
                CtlKey(Key.D,true);CtlButton(1001,JoyButton.B,true);CtlButton(1001,JoyButton.B,false);await Frames(10);CtlKey(Key.D,false);await Frames(2);
            }
            Check(State.GetProperty("bombs").GetArrayLength()==3,"Switch printed-A taps place two more available bombs through the production bridge");
            Check(State.GetProperty("bombs").EnumerateArray().All(b=>b.GetProperty("state").GetInt32()==0),"moving primary taps produce a stationary three-bomb trail");
            Check(Cue("bomb")==bombCue+3,"three accepted taps give exactly three placement sounds");
            await Capture("placement-three-bomb-trail",0);
            int capacity=Player.GetProperty("bombs").GetInt32();
            for(int count=3;count<capacity;count++)
            {CtlKey(Key.D,true);CtlButton(1001,JoyButton.B,true);CtlButton(1001,JoyButton.B,false);await Frames(10);CtlKey(Key.D,false);await Frames(2);}
            CtlButton(1001,JoyButton.B,true);CtlButton(1001,JoyButton.B,false);await Frames(3);
            Check(State.GetProperty("bombs").GetArrayLength()==capacity&&State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="bomb-blocked"&&e.GetProperty("reason").GetString()=="capacity"),"capacity rejection is explained without creating an extra bomb");
            if(closeup!=null){battleCamera!.MakeCurrent();closeup.QueueFree();closeup=null;}
            await HandlingPractice();_root.InjectInput(0,null);await ControllerFocusReady();await Frames(3);
            CtlButton(1001,JoyButton.B,true);CtlButton(1001,JoyButton.B,false);await Frames(3);
            Check(State.GetProperty("bombs").GetArrayLength()==1,"stationary printed A places one near-foot bomb");
            CtlButton(1001,JoyButton.B,true);CtlButton(1001,JoyButton.B,false);
            await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="kick"),"second stationary printed A deliberately kicks that same bomb",20);
            Check(State.GetProperty("bombs").GetArrayLength()==1,"deliberate second-A kick never creates an overlapping duplicate");
            await Capture("placement-second-a-kick",0);
        }
        finally
        {
            if(closeup!=null){battleCamera?.MakeCurrent();closeup.QueueFree();}
            CtlReleaseAll();if(_root.ActiveSession!=null)_root.Back();hub.RestorePhysicalDevicesForPlaytest();await Frames(3);
        }
    }
}
