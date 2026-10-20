using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit;
using BlastCircuit.InputDevices;
using System.Text.Json;
using EngineInput=Godot.Input;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private static readonly GamepadDevice[] QaPads=[new(12,"qa-xbox-series","Xbox Series Controller"),new(1001,"qa-switch2-pro","Switch 2 Pro Controller"),new(14,"qa-dualsense","DualSense Wireless Controller")];
    private static readonly Key[] QaKeys=[Key.W,Key.A,Key.S,Key.D,Key.J,Key.K,Key.L,Key.Space,Key.Shift,Key.R,Key.Left,Key.Right,Key.Up,Key.Down,Key.N,Key.M,Key.Comma,Key.Enter,Key.Ctrl,Key.Backspace,Key.Kp0,Key.Kp1,Key.Kp2];
    private static readonly JoyButton[] QaButtons=[JoyButton.A,JoyButton.B,JoyButton.X,JoyButton.Y,JoyButton.LeftShoulder,JoyButton.RightShoulder,JoyButton.DpadLeft,JoyButton.DpadRight,JoyButton.DpadUp,JoyButton.DpadDown,JoyButton.Start];
    private async Task ControllerInventory()
    {
        await Frames(20);
        var inventory=new
        {
            evidenceMode="inventory-only; no physical-button confirmation",serialNumbersRecorded=false,
            godotDevices=EngineInput.GetConnectedJoypads().Select(id=>new {runtimeId=id,name=EngineInput.GetJoyName(id),guid=EngineInput.GetJoyGuid(id),mapped=EngineInput.IsJoyKnown(id)}).ToArray(),
            hubDevices=ControllerHub.Current.Devices.Select(d=>new {runtimeId=d.Device.RuntimeId,name=d.Device.Name,guid=d.Device.Guid,family=d.Family.ToString(),mapped=d.Mapped,backend=d.Backend}).ToArray(),
            backend=ControllerHub.Current.BackendStatus
        };
        File.WriteAllText(Path.Combine(_output,"controller-inventory.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions{WriteIndented=true}));
        Event("controller-inventory",inventory);Check(true,"controller inventory captured without claiming physical button testing");
        _root.Navigate("Controls");await Frames(3);Check(_root.ScreenName=="Controls","real controller tester screen opens");await Capture("controller-inventory");_root.Back();await Frames(3);
    }
    private async Task ControllerInputs()
    {
        await ControllerFocusReady();
        await ControllerInventory();var hub=ControllerHub.Current;
        Event("controller-test-mode",new {inputSource="synthetic Godot keyboard and canonical gamepad events",deviceRegistry="playtest substitution",physicalButtonsTested=false});
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest(QaPads);await Frames(3);await ControllerMenuEvents();
            float keyboard=await ControllerMovementTrial(null);
            foreach(var pad in QaPads)
            {
                float analog=await ControllerMovementTrial(pad);Check(Math.Abs(analog-keyboard)<.025f,$"{pad.Name}: unit-speed analog movement equals keyboard movement");await ControllerBombEvents(pad);
            }
            await ControllerKeyboardSeats();await ControllerHotplugEvents();await ControllerFocusEvents();
            _root.Navigate("Controls");await Frames(3);await Capture("controllers-synthetic-tester");
        }
        finally
        {
            CtlReleaseAll();if(_root.ActiveSession!=null)_root.Back();hub.RestorePhysicalDevicesForPlaytest();
            Event("controller-test-cleanup",new {physicalDeviceDiscoveryRestored=true});_root.Navigate("Modes");await Frames(3);
        }
    }
    private async Task ControllerFocusReady()
    {
        if(_headless)return;
        GetWindow().GrabFocus();
        var wall=System.Diagnostics.Stopwatch.StartNew();double? stableSince=null;bool stable=false;double nextActivation=0;
        while(wall.Elapsed.TotalSeconds<4)
        {
            if(GetWindow().HasFocus()&&ControllerHub.Current.HasApplicationFocus)
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.15){stable=true;break;}
            }
            else
            {
                stableSince=null;
                // macOS may finish a native resize after the initial activation request.
                // Re-request actual OS activation; never synthesize application-focus flags.
                if(wall.Elapsed.TotalSeconds>=nextActivation)
                {
                    DisplayServer.WindowMoveToForeground(GetWindow().GetWindowId());
                    nextActivation=wall.Elapsed.TotalSeconds+.35;
                }
            }
            await Frames(1);await Task.Delay(20);
        }
        Event("native-input-focus",new {stable,window=GetWindow().HasFocus(),application=ControllerHub.Current.HasApplicationFocus});
        Check(stable,"controller event trial has stable real window/application focus");
    }
    private async Task ControllerObserve()
    {
        Check(!_headless,"physical observation requires graphical controller discovery");
        _root.Navigate("Controls");await Frames(6);
        var observed=new Dictionary<int,HashSet<string>>();
        var devices=ControllerHub.Current.Devices.ToDictionary(d=>d.Device.RuntimeId);
        object Report()=>new {evidenceMode="observed physical input, no injected events",devices=devices.Values.Select(d=>new
        {
            name=d.Device.Name,family=d.Family.ToString(),backend=d.Backend,
            actions=observed.TryGetValue(d.Device.RuntimeId,out var actions)?actions.Order().ToArray():[],
            physicalInputObserved=observed.ContainsKey(d.Device.RuntimeId)
        }).ToArray()};
        void Save()=>File.WriteAllText(Path.Combine(_output,"physical-input.json"),JsonSerializer.Serialize(Report(),new JsonSerializerOptions{WriteIndented=true}));
        void Record(int id,string action,float value)
        {
            var device=ControllerHub.Current.Devices.FirstOrDefault(d=>d.Device.RuntimeId==id&&d.Backend!="Synthetic QA");
            if(device==null)return;devices[id]=device;
            if(!observed.TryGetValue(id,out var actions))observed[id]=actions=[];
            if(actions.Add(action)){Event("physical-input",new {name=device.Device.Name,action,value});Save();}
        }
        ControllerHub.Current.ObservedInput+=Record;
        try
        {
            await Capture("controllers-physical-ready");
            Save();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<60&&_root.ScreenName=="Controls"){await Frames(1);await Task.Delay(30);}
            var report=Report();Save();
            Event("physical-input-observation",report);
            Check(true,"physical observations recorded separately from simulated tests");
            await Capture("controllers-physical-observed");
        }
        finally{ControllerHub.Current.ObservedInput-=Record;_root.Navigate("Modes");}
    }
    private async Task ControllerMenuEvents()
    {
        _root.Navigate("Modes");await Frames(3);FindButton("LOCAL BATTLE").GrabFocus();await Frames(1);
        CtlAxis(12,JoyAxis.LeftY,.3f);await Frames(3);Check(FindButton("LOCAL BATTLE").HasFocus(),"menu analog deadzone rejects .3 drift");CtlAxis(12,JoyAxis.LeftY,0);
        CtlButton(12,JoyButton.DpadDown,true);await Frames(2);CtlButton(12,JoyButton.DpadDown,false);await Frames(2);
        Check(FindButton("ONLINE BATTLE").HasFocus(),"canonical D-pad navigates real menu");
        Event("controller-menu-map",new {accept=EngineInput.IsActionPressed("ui_accept"),events=InputMap.ActionGetEvents("ui_accept").Select(e=>e.AsText()).ToArray()});
        CtlButton(12,JoyButton.A,true);await Frames(1);Event("controller-menu-pressed",new {screen=_root.ScreenName,accept=EngineInput.IsActionPressed("ui_accept")});CtlButton(12,JoyButton.A,false);await Frames(4);Event("controller-menu-released",new {screen=_root.ScreenName});Check(_root.ScreenName=="Online","canonical south confirms real online menu");
        CtlButton(12,JoyButton.B,true);await Frames(1);CtlButton(12,JoyButton.B,false);await Frames(4);Check(_root.ScreenName=="Modes","canonical east backs out of menu");await Capture("controllers-menu-events");
    }
    private async Task<float> ControllerMovementTrial(GamepadDevice? pad)
    {
        await ControllerFocusReady();
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest(pad==null?[]:[pad]);
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"controller trial reaches real fight",150);await Frames(4);
        float start=CtlPlayer(0).GetProperty("x").GetSingle();
        if(pad==null)CtlKey(Key.D,true);else CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.75f);
        Event("controller-intent",new {source=pad?.Name??"Keyboard",windowFocus=GetWindow().HasFocus(),applicationFocus=ControllerHub.Current.HasApplicationFocus,keyboard=PlayerControls.Keyboard(0),intent=PlayerControls.Read(0,1)});
        await Frames(8);
        if(pad==null)CtlKey(Key.D,false);else CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);await Frames(4);
        float distance=CtlPlayer(0).GetProperty("x").GetSingle()-start;Check(distance>.6f,(pad?.Name??"Keyboard")+": production events move authoritative player");
        Event("controller-movement",new {source=pad?.Name??"Keyboard",synthetic=true,distance});await Capture("controllers-move-"+(pad?.RuntimeId.ToString()??"keyboard"));_root.Back();await Frames(3);return distance;
    }
    private async Task ControllerBombEvents(GamepadDevice pad)
    {
        await ControllerFocusReady();
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest([pad]);CtlButton(pad.RuntimeId,JoyButton.A,true);
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"held-confirm trial reaches fight",150);
        Check(State.GetProperty("bombs").GetArrayLength()==0,pad.Name+": held confirm cannot become gameplay bomb");
        CtlButton(pad.RuntimeId,JoyButton.A,false);await Frames(4);
        await ControllerFocusReady();
        CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.3f);
        var deadzoneIntent=PlayerControls.Read(0,1);
        Event("controller-deadzone-probe",new {pad=pad.Name,windowFocus=GetWindow().HasFocus(),applicationFocus=ControllerHub.Current.HasApplicationFocus,intent=deadzoneIntent});
        Check(deadzoneIntent.X>.99f,"gameplay deadzone accepts .3 at unit speed");CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);
        CtlButton(pad.RuntimeId,JoyButton.DpadRight,true);Check(PlayerControls.Read(0,1).X>.99f,"D-pad provides gameplay movement");CtlButton(pad.RuntimeId,JoyButton.DpadRight,false);
        foreach(var button in new[]{JoyButton.B,JoyButton.RightShoulder})
        {CtlButton(pad.RuntimeId,button,true);Check(PlayerControls.Read(0,1).R,"remote alias reaches command: "+button);CtlButton(pad.RuntimeId,button,false);_ = PlayerControls.Read(0,1);}
        CtlButton(pad.RuntimeId,JoyButton.X,true);Check(PlayerControls.Read(0,1).B,"west face button carries");CtlButton(pad.RuntimeId,JoyButton.X,false);
        CtlButton(pad.RuntimeId,JoyButton.A,true);CtlButton(pad.RuntimeId,JoyButton.A,false);await Frames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==1,pad.Name+": sub-frame south tap places one real bomb");
        CtlButton(pad.RuntimeId,JoyButton.A,true);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,1);await Frames(10);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,pad.Name+": holding bomb while moving never auto-repeats");
        CtlButton(pad.RuntimeId,JoyButton.A,false);await Frames(2);CtlButton(pad.RuntimeId,JoyButton.A,true);await Frames(2);CtlButton(pad.RuntimeId,JoyButton.A,false);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==2,pad.Name+": release and repress creates second bomb");
        CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,true);await Frames(2);Check(CtlPlayer(0).GetProperty("carrying").GetString()=="bomb",pad.Name+": left shoulder lifts real bomb");
        CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,false);await Frames(2);Check(CtlPlayer(0).GetProperty("carrying").ValueKind==JsonValueKind.Null,pad.Name+": release throws bomb");
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3),pad.Name+": thrown bomb enters flight");
        await Capture("controllers-bomb-"+pad.RuntimeId);CtlReleaseAll();_root.Back();await Frames(3);
    }
    private async Task ControllerKeyboardSeats()
    {
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest([]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),2,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"two keyboard seats reach fight",150);await Frames(4);
        float first=CtlPlayer(0).GetProperty("x").GetSingle(),second=CtlPlayer(1).GetProperty("x").GetSingle();CtlKey(Key.D,true);CtlKey(Key.Left,true);await Frames(8);CtlKey(Key.D,false);CtlKey(Key.Left,false);await Frames(4);
        float p1=CtlPlayer(0).GetProperty("x").GetSingle()-first,p2=second-CtlPlayer(1).GetProperty("x").GetSingle();Check(p1>.6f&&Math.Abs(p1-p2)<.025f,"P1 WASD and P2 arrows share authoritative speed");
        foreach(var entry in new[]{(0,Key.J,0),(0,Key.K,1),(0,Key.L,2),(1,Key.N,0),(1,Key.M,1),(1,Key.Comma,2),(0,Key.Space,0),(0,Key.Shift,1),(0,Key.R,2),(1,Key.Enter,0),(1,Key.Ctrl,1),(1,Key.Backspace,2),(1,Key.Kp0,0),(1,Key.Kp1,1),(1,Key.Kp2,2)})
        {
            CtlKey(entry.Item2,true);var actual=PlayerControls.Read(entry.Item1,2);bool active=entry.Item3==0?actual.A:entry.Item3==1?actual.B:actual.R;Check(active,$"keyboard seat{entry.Item1+1} {entry.Item2} reaches correct action");
            var untouched=PlayerControls.Read(1-entry.Item1,2);Check(!untouched.A&&!untouched.B&&!untouched.R,$"keyboard {entry.Item2} never leaks to other seat");CtlKey(entry.Item2,false);_ = PlayerControls.Read(entry.Item1,2);
        }
        CtlKey(Key.J,true);CtlKey(Key.J,false);CtlKey(Key.N,true);CtlKey(Key.N,false);await Frames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==2,"both compact keyboard bomb taps reach authority independently");await Capture("controllers-two-keyboards");_root.Back();await Frames(3);
    }
    private async Task ControllerHotplugEvents()
    {
        var hub=ControllerHub.Current;CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[0],QaPads[1]]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),4,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"four-seat hotplug trial reaches fight",150);await Frames(4);
        Check(hub.Bindings!.DeviceIdForSeat(0)==null&&hub.Bindings.DeviceIdForSeat(1)==null&&hub.Bindings.DeviceIdForSeat(2)==12&&hub.Bindings.DeviceIdForSeat(3)==1001,"four humans reserve keyboard seats and independent pads");
        CtlAxis(12,JoyAxis.LeftX,.75f);Check(PlayerControls.Read(2,4).X>.99f&&PlayerControls.Read(0,4).X==0&&PlayerControls.Read(1,4).X==0,"assigned pad moves only its own seat");CtlAxis(12,JoyAxis.LeftX,0);
        CtlButton(1001,JoyButton.LeftShoulder,true);Check(PlayerControls.Read(3,4).B,"second pad holds carry before unrelated disconnect");
        hub.OverrideDevicesForPlaytest([QaPads[1],QaPads[0]]);Check(hub.Bindings.DeviceIdForSeat(2)==12&&hub.Bindings.DeviceIdForSeat(3)==1001,"enumeration reorder preserves seats");
        hub.OverrideDevicesForPlaytest([QaPads[1]]);Check(hub.Bindings.DeviceIdForSeat(2)==null&&hub.Bindings.DeviceIdForSeat(3)==1001,"disconnect reserves empty seat without shifting another pad");
        Check(PlayerControls.Read(3,4).B,"unrelated disconnect preserves held carry");Check(PlayerControls.Read(2,4)==default,"disconnected seat reads neutral");
        hub.OverrideDevicesForPlaytest([QaPads[1],new(14,QaPads[0].Guid,QaPads[0].Name)]);Check(hub.Bindings.DeviceIdForSeat(2)==14&&hub.Bindings.DeviceIdForSeat(3)==1001,"reconnect restores unique identity with new runtime ID");
        CtlReleaseAll();await Capture("controllers-stable-hotplug");_root.Back();await Frames(3);
    }
    private async Task ControllerFocusEvents()
    {
        var hub=ControllerHub.Current;CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[0]]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"focus trial reaches fight",150);await Frames(4);CtlKey(Key.D,true);CtlKey(Key.J,true);
        hub.Notification((int)Node.NotificationApplicationFocusOut);Check(PlayerControls.Read(0,1)==default,"focus loss returns neutral movement and actions");
        hub.Notification((int)Node.NotificationApplicationFocusIn);Check(!PlayerControls.Read(0,1).A,"held bomb cannot trigger on focus restoration");
        CtlReleaseAll();_ = PlayerControls.Read(0,1);CtlKey(Key.J,true);Check(PlayerControls.Read(0,1).A,"fresh press rearms after focus restoration");CtlReleaseAll();_root.Back();await Frames(3);
    }
    private JsonElement CtlPlayer(int slot)=>State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("slot").GetInt32()==slot);
    private static void CtlKey(Key key,bool pressed){EngineInput.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed,Echo=false});EngineInput.FlushBufferedEvents();}
    private static void CtlButton(int device,JoyButton button,bool pressed){EngineInput.ParseInputEvent(new InputEventJoypadButton {Device=device,ButtonIndex=button,Pressed=pressed});EngineInput.FlushBufferedEvents();}
    private static void CtlAxis(int device,JoyAxis axis,float value){EngineInput.ParseInputEvent(new InputEventJoypadMotion {Device=device,Axis=axis,AxisValue=value});EngineInput.FlushBufferedEvents();}
    private static void CtlReleaseAll()
    {
        foreach(var key in QaKeys)CtlKey(key,false);
        foreach(var pad in QaPads){CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,0);foreach(var button in QaButtons)CtlButton(pad.RuntimeId,button,false);}
    }
}
