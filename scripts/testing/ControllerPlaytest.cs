using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
using EngineInput=Godot.Input;
using BlastCircuit.InputDevices;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private static readonly GamepadDevice[] QaPads=[new(12,"qa-xbox-series","Xbox Series Controller"),new(1001,"qa-switch2-pro","Switch 2 Pro Controller"),new(14,"qa-dualsense","DualSense Wireless Controller")];
    private static readonly Key[] QaKeys=[Key.W,Key.A,Key.S,Key.D,Key.J,Key.K,Key.L,Key.I,Key.O,Key.Period,Key.Kp3,Key.Space,Key.Shift,Key.R,Key.Left,Key.Right,Key.Up,Key.Down,Key.N,Key.M,Key.Comma,Key.Enter,Key.Ctrl,Key.Backspace,Key.Kp0,Key.Kp1,Key.Kp2];
    private static readonly JoyButton[] QaButtons=[JoyButton.A,JoyButton.B,JoyButton.X,JoyButton.Y,JoyButton.LeftShoulder,JoyButton.RightShoulder,JoyButton.DpadLeft,JoyButton.DpadRight,JoyButton.DpadUp,JoyButton.DpadDown,JoyButton.Start];
    private async Task ControllerInventory()
    {
        await ControllerFrames(20);
        var inventory=new
        {
            evidenceMode="inventory-only; no physical-button confirmation",serialNumbersRecorded=false,
            godotDevices=EngineInput.GetConnectedJoypads().Select(id=>new {runtimeId=id,name=EngineInput.GetJoyName(id),guid=EngineInput.GetJoyGuid(id),mapped=EngineInput.IsJoyKnown(id)}).ToArray(),
            hubDevices=ControllerHub.Current.Devices.Select(d=>new {runtimeId=d.Device.RuntimeId,name=d.Device.Name,guid=d.Device.Guid,family=d.Family.ToString(),mapped=d.Mapped,backend=d.Backend}).ToArray(),
            backend=ControllerHub.Current.BackendStatus
        };
        File.WriteAllText(Path.Combine(_output,"controller-inventory.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions{WriteIndented=true}));
        Event("controller-inventory",inventory);Check(true,"controller inventory captured without claiming physical button testing");
        _root.Navigate("Controls");await ControllerFrames(3);Check(_root.ScreenName=="Controls","real controller tester screen opens");await Capture("controller-inventory");_root.Back();await ControllerFrames(3);
    }
    private bool _controllerFocusTrial;
    private sealed class ControllerFocusInterruptedException : Exception
    {
        public ControllerFocusInterruptedException():base("Native window/application focus left during the controller trial; this input interval is invalid."){}
    }
    private async Task ControllerFrames(int count)
    {
        void VerifyFocus()
        {
            if(_controllerFocusTrial&&!_headless&&(!GetWindow().HasFocus()||!ControllerHub.Current.HasApplicationFocus))
            {
                Event("controller-focus-interrupted",new {window=GetWindow().HasFocus(),application=ControllerHub.Current.HasApplicationFocus,screen=_root.ScreenName});
                throw new ControllerFocusInterruptedException();
            }
        }
        VerifyFocus();
        for(int i=0;i<count;i++){await Frames(1);VerifyFocus();}
    }
    private async Task ControllerInputs()=>await WithControllerFocusRetries(ControllerInputsAttempt,"controllers");
    private async Task WithControllerFocusRetries(Func<Task> scenario,string context)
    {
        for(int attempt=1;attempt<=3;attempt++)
        {
            int checksBefore=_checks.Count,capturesBefore=_screenshots.Count;
            try
            {
                _controllerFocusTrial=true;
                await scenario();
                Event("controller-focus-result",new {context,attempts=attempt,interruptedAttempts=attempt-1});return;
            }
            catch(ControllerFocusInterruptedException) when(attempt<3)
            {
                int discarded=_checks.Count-checksBefore;
                _checks.RemoveRange(checksBefore,discarded);
                _screenshots.RemoveRange(capturesBefore,_screenshots.Count-capturesBefore);
                Event("controller-focus-retry",new {context,attempt,discardedAssertions=discarded,reason="observed native focus loss; restart the complete controller scenario with fresh session and released inputs"});
                await ControllerFocusReady();
            }
            finally {_controllerFocusTrial=false;}
        }
    }
    private async Task ControllerInputsAttempt()
    {
        await ControllerFocusReady();
        await ControllerInventory();var hub=ControllerHub.Current;
        Event("controller-test-mode",new {inputSource="synthetic Godot keyboard and canonical gamepad events",deviceRegistry="playtest substitution",switchEventPath="production native publication bridge",physicalButtonsTested=false});
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest(QaPads);await ControllerFrames(3);await ControllerMenuEvents();
            float keyboard=await ControllerMovementTrial(null);
            foreach(var pad in QaPads)
            {
                float analog=await ControllerMovementTrial(pad);Check(Math.Abs(analog-keyboard)<.025f,$"{pad.Name}: unit-speed analog movement equals keyboard movement");await ControllerBombEvents(pad);
            }
            await ControllerKeyboardSeats();await ControllerHotplugEvents();await ControllerFocusEvents();await ControllerBoundMenuEvents();await ControllerFaceMenuEvents();
            _root.Navigate("Controls");await ControllerFrames(3);await Capture("controllers-synthetic-tester");
        }
        finally
        {
            _controllerFocusTrial=false;CtlReleaseAll();if(_root.ActiveSession!=null)_root.Back();hub.RestorePhysicalDevicesForPlaytest();
            Event("controller-test-cleanup",new {physicalDeviceDiscoveryRestored=true});_root.Navigate("Modes");await ControllerFrames(3);
        }
    }
    private async Task ControllerFocusReady()
    {
        if(_headless)return;
        if(!GetWindow().HasFocus()||!ControllerHub.Current.HasApplicationFocus)GetWindow().GrabFocus();
        var wall=System.Diagnostics.Stopwatch.StartNew();double? stableSince=null;bool stable=false;double nextActivation=0;
        while(wall.Elapsed.TotalSeconds<4)
        {
            if(GetWindow().HasFocus()&&ControllerHub.Current.HasApplicationFocus)
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.5){stable=true;break;}
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
        Event("native-input-focus",new {stable,stableSeconds=.5,window=GetWindow().HasFocus(),application=ControllerHub.Current.HasApplicationFocus});
        Check(stable,"controller event trial has stable real window/application focus");
    }
    private async Task ControllerObserve()
    {
        Check(!_headless,"physical observation requires graphical controller discovery");
        _root.Navigate("Controls");await ControllerFrames(6);
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
        using var axisTrace=new StreamWriter(Path.Combine(_output,"physical-axis-trace.jsonl")){AutoFlush=true};
        var axisWatch=System.Diagnostics.Stopwatch.StartNew();int axisSamples=0;
        void RecordAxis(int id,JoyAxis axis,float value)
        {
            var device=ControllerHub.Current.Devices.FirstOrDefault(d=>d.Device.RuntimeId==id&&d.Backend!="Synthetic QA");
            if(device==null||axisSamples>=60000)return;
            axisSamples++;
            axisTrace.WriteLine(JsonSerializer.Serialize(new {seconds=axisWatch.Elapsed.TotalSeconds,name=device.Device.Name,backend=device.Backend,axis=axis.ToString(),value,
                menuActive=Math.Abs(value)>=MenuStickNavigation.PressThreshold,gameplayActive=Math.Abs(value)>InputPolicy.StickDeadzone}));
        }
        ControllerHub.Current.ObservedInput+=Record;ControllerHub.Current.ObservedAxis+=RecordAxis;
        try
        {
            await Capture("controllers-physical-ready");
            Save();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<120&&_root.ScreenName=="Controls"){await ControllerFrames(1);await Task.Delay(30);}
            var report=Report();Save();
            Event("physical-input-observation",report);
            Check(true,"physical observations recorded separately from simulated tests");
            await Capture("controllers-physical-observed");
        }
        finally{ControllerHub.Current.ObservedInput-=Record;ControllerHub.Current.ObservedAxis-=RecordAxis;Event("physical-axis-trace",new {axisSamples,path="physical-axis-trace.jsonl"});_root.Navigate("Modes");}
    }
    private async Task ControllerMenuEvents()
    {
        await ControllerFocusReady();
        _root.Navigate("Modes");await ControllerFrames(3);FindButton("LOCAL BATTLE").GrabFocus();await ControllerFrames(1);
        foreach(var pad in QaPads)
        {
            await ControllerFocusReady();
            int id=pad.RuntimeId;
            CtlAxis(id,JoyAxis.LeftY,.3f);await ControllerFrames(3);Check(FindButton("LOCAL BATTLE").HasFocus(),pad.Name+": menu analog deadzone rejects .3 drift");CtlAxis(id,JoyAxis.LeftY,0);
            // Real sticks cross the threshold gradually, then may send no reports while held.
            foreach(float value in new[]{.35f,.42f,.48f,.65f}){CtlAxis(id,JoyAxis.LeftY,value);await ControllerFrames(1);}
            Event("menu-navigation-focus",new {pad=pad.Name,windowFocus=GetWindow().HasFocus(),applicationFocus=ControllerHub.Current.HasApplicationFocus,enabled=ControllerHub.Current.MenuNavigationEnabled,tester=ControllerHub.Current.TesterActive,focus=(GetViewport().GuiGetFocusOwner() as Button)?.Text});
            Check(FindButton("ONLINE BATTLE").HasFocus(),pad.Name+": gradual analog threshold crossing moves menu exactly once");
            await ControllerFrames(14);
            Check(FindButton("TUTORIAL").HasFocus(),pad.Name+": held analog direction repeats without more motion events");
            CtlAxis(id,JoyAxis.LeftY,-.65f);await ControllerFrames(2);
            Check(FindButton("ONLINE BATTLE").HasFocus(),pad.Name+": analog reversal navigates immediately");
            foreach(float value in new[]{-.44f,-.35f,-.43f}){CtlAxis(id,JoyAxis.LeftY,value);await ControllerFrames(1);}
            Check(FindButton("ONLINE BATTLE").HasFocus(),pad.Name+": threshold jitter never creates extra navigation presses");
            CtlAxis(id,JoyAxis.LeftY,0);await ControllerFrames(2);CtlAxis(id,JoyAxis.LeftY,-.65f);await ControllerFrames(2);
            Check(FindButton("LOCAL BATTLE").HasFocus(),pad.Name+": neutral release rearms the same direction");
            CtlAxis(id,JoyAxis.LeftY,0);await ControllerFrames(2);
            CtlAxis(id,JoyAxis.LeftX,.6f);CtlAxis(id,JoyAxis.LeftY,.65f);await ControllerFrames(2);
            Check(FindButton("ONLINE BATTLE").HasFocus(),pad.Name+": diagonal stick selects a single menu direction");
            CtlAxis(id,JoyAxis.LeftX,.68f);await ControllerFrames(2);
            Check(FindButton("ONLINE BATTLE").HasFocus(),pad.Name+": near-diagonal noise keeps its navigation axis");
            CtlAxis(id,JoyAxis.LeftX,0);CtlAxis(id,JoyAxis.LeftY,0);await ControllerFrames(2);FindButton("LOCAL BATTLE").GrabFocus();await ControllerFrames(1);
        }
        CtlButton(12,JoyButton.DpadDown,true);await ControllerFrames(2);CtlButton(12,JoyButton.DpadDown,false);await ControllerFrames(2);
        Check(FindButton("ONLINE BATTLE").HasFocus(),"canonical D-pad navigates real menu");
        Event("controller-menu-map",new {accept=EngineInput.IsActionPressed("ui_accept"),events=InputMap.ActionGetEvents("ui_accept").Select(e=>e.AsText()).ToArray()});
        CtlButton(12,JoyButton.A,true);await ControllerFrames(1);Event("controller-menu-pressed",new {screen=_root.ScreenName,accept=EngineInput.IsActionPressed("ui_accept")});CtlButton(12,JoyButton.A,false);await ControllerFrames(4);Event("controller-menu-released",new {screen=_root.ScreenName});Check(_root.ScreenName=="Online","canonical south confirms real online menu");
        CtlButton(12,JoyButton.B,true);await ControllerFrames(1);CtlButton(12,JoyButton.B,false);await ControllerFrames(4);Check(_root.ScreenName=="Modes","canonical east backs out of menu");await Capture("controllers-menu-events");
    }
    private async Task ControllerFaceMenuEvents()
    {
        ControllerHub.Current.OverrideDevicesForPlaytest(QaPads);
        foreach(var pad in QaPads)
        {
            await ControllerFocusReady();CtlReleaseAll();_root.Navigate("Modes");await ControllerFrames(3);
            var family=ControllerProfile.Family(pad.Name);
            var confirm=ControllerProfile.ConfirmButton(family);var cancel=ControllerProfile.CancelButton(family);
            Check(ControllerProfile.Menu(family).Contains(ControllerProfile.ButtonName(family,confirm)+"  CONFIRM"),pad.Name+": confirm prompt follows the device mapping");
            FindButton("LOCAL BATTLE").GrabFocus();await ControllerFrames(1);
            CtlButton(pad.RuntimeId,confirm,true);await ControllerFrames(6);CtlButton(pad.RuntimeId,confirm,false);await ControllerFrames(3);
            Check(_root.ScreenName=="Roster",pad.Name+": printed confirm enters roster once without cancelling");
            CtlButton(pad.RuntimeId,cancel,true);await ControllerFrames(6);CtlButton(pad.RuntimeId,cancel,false);await ControllerFrames(3);
            Check(_root.ScreenName=="Modes",pad.Name+": printed back exits once without confirming another menu");
        }
    }
    private async Task<float> ControllerMovementTrial(GamepadDevice? pad)
    {
        await ControllerFocusReady();
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest(pad==null?[]:[pad]);
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"controller trial reaches real fight",150);await ControllerFrames(4);
        // Countdown/scene activation can outlast the earlier focus handshake.
        // Establish real OS focus immediately before measuring native events.
        await ControllerFocusReady();await ControllerFrames(2);
        float start=CtlPlayer(0).GetProperty("x").GetSingle();
        if(pad==null)CtlKey(Key.D,true);else CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.75f);
        Event("controller-intent",new {source=pad?.Name??"Keyboard",windowFocus=GetWindow().HasFocus(),applicationFocus=ControllerHub.Current.HasApplicationFocus,keyboard=PlayerControls.Keyboard(0),intent=PlayerControls.Read(0,1)});
        await ControllerFrames(8);
        if(pad==null)CtlKey(Key.D,false);else CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);await ControllerFrames(4);
        float distance=CtlPlayer(0).GetProperty("x").GetSingle()-start;Check(distance>.6f,(pad?.Name??"Keyboard")+": production events move authoritative player");
        Event("controller-movement",new {source=pad?.Name??"Keyboard",synthetic=true,distance});
        await Capture("controllers-move-"+(pad?.RuntimeId.ToString()??"keyboard"));
        if(pad!=null)
        {
            for(int step=0;step<120;step++)
            {
                float angle=step*Mathf.Tau/120;float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                float radius=.28f+.65f*(step%20)/19;
                CtlAxis(pad.RuntimeId,JoyAxis.LeftX,x*radius);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,z*radius);
                await ControllerFrames(1);var intent=PlayerControls.Read(0,1);
                Check(Math.Abs(intent.X-x)<.001f&&Math.Abs(intent.Z-z)<.001f,pad.Name+": gradual 360 stick direction remains continuous at step "+step);
            }
            CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.3f);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,.4f);
            for(int frame=0;frame<60;frame++)
            {
                await ControllerFrames(1);var intent=PlayerControls.Read(0,1);
                Check(Math.Abs(intent.X-.6f)<.001f&&Math.Abs(intent.Z-.8f)<.001f,pad.Name+": sustained diagonal retains full-speed state without new reports at frame "+frame);
            }
            CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,0);
        }
        _root.Back();await ControllerFrames(3);return distance;
    }
    private async Task ControllerBombEvents(GamepadDevice pad)
    {
        var family=ControllerProfile.Family(pad.Name);
        var bomb=ControllerProfile.BombButton(family);var carry=ControllerProfile.CarryButton(family);var remote=ControllerProfile.RemoteButton(family);
        Check(ControllerProfile.ButtonName(family,bomb)==(family==ControllerFamily.PlayStation?"Cross":"A"),pad.Name+": primary prompt matches the physical printed button");
        Check(ControllerProfile.ButtonName(family,carry)==(family==ControllerFamily.PlayStation?"Square":family==ControllerFamily.Nintendo?"Y":"X"),pad.Name+": face carry uses the west button for a comfortable alternate grip");
        await ControllerFocusReady();
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest([pad]);CtlButton(pad.RuntimeId,bomb,true);
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"held-confirm trial reaches fight",150);
        Check(State.GetProperty("bombs").GetArrayLength()==0,pad.Name+": held confirm cannot become gameplay bomb");
        CtlButton(pad.RuntimeId,bomb,false);await ControllerFrames(4);
        await ControllerFocusReady();
        CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.3f);
        var deadzoneIntent=PlayerControls.Read(0,1);
        Event("controller-deadzone-probe",new {pad=pad.Name,windowFocus=GetWindow().HasFocus(),applicationFocus=ControllerHub.Current.HasApplicationFocus,intent=deadzoneIntent});
        Check(deadzoneIntent.X>.99f,"gameplay deadzone accepts .3 at unit speed");
        CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.3f);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,.4f);
        var diagonal=PlayerControls.Read(0,1);Check(Math.Abs(diagonal.X-.6f)<.001f&&Math.Abs(diagonal.Z-.8f)<.001f,pad.Name+": diagonal preserves analog direction at unit speed");
        CtlAxis(pad.RuntimeId,JoyAxis.LeftX,-.3f);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,-.4f);
        var reverse=PlayerControls.Read(0,1);Check(Math.Abs(reverse.X+.6f)<.001f&&Math.Abs(reverse.Z+.8f)<.001f,pad.Name+": gameplay reversal reaches production read without stale direction");
        CtlAxis(pad.RuntimeId,JoyAxis.LeftY,0);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);
        CtlButton(pad.RuntimeId,JoyButton.DpadRight,true);Check(PlayerControls.Read(0,1).X>.99f,"D-pad provides gameplay movement");CtlButton(pad.RuntimeId,JoyButton.DpadRight,false);
        foreach(var button in new[]{remote})
        {CtlButton(pad.RuntimeId,button,true);Check(PlayerControls.Read(0,1).R,"remote alias reaches command: "+button);CtlButton(pad.RuntimeId,button,false);_ = PlayerControls.Read(0,1);}
        CtlButton(pad.RuntimeId,JoyButton.RightShoulder,true);CtlButton(pad.RuntimeId,JoyButton.RightShoulder,false);
        var stopIntent=PlayerControls.Read(0,1);Check(stopIntent.Stop&&!stopIntent.R,pad.Name+": sub-frame shoulder tap stops without detonating");
        Check(!PlayerControls.Read(0,1).Stop,pad.Name+": stop tap is consumed once");
        CtlButton(pad.RuntimeId,carry,true);var carryIntent=PlayerControls.Read(0,1);Check(carryIntent.B&&!carryIntent.A&&!carryIntent.R,pad.Name+": carry face button reaches only carry");CtlButton(pad.RuntimeId,carry,false);
        CtlButton(pad.RuntimeId,bomb,true);CtlButton(pad.RuntimeId,bomb,false);await ControllerFrames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==1,pad.Name+": sub-frame primary tap places one real bomb");
        // Start fresh so this press's count is independent of placement rules at the first tap's position.
        _root.Back();await ControllerFrames(2);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"held-primary trial reaches fight",150);await ControllerFrames(4);
        CtlButton(pad.RuntimeId,bomb,true);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,1);await ControllerFrames(10);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);await ControllerFrames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,pad.Name+": holding bomb while moving never auto-repeats");
        CtlButton(pad.RuntimeId,bomb,false);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,1);await ControllerFrames(2);CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);
        CtlButton(pad.RuntimeId,bomb,true);await ControllerFrames(2);CtlButton(pad.RuntimeId,bomb,false);await ControllerFrames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==2,pad.Name+": release and repress creates second bomb");
        CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,true);await ControllerFrames(2);Check(CtlPlayer(0).GetProperty("carrying").GetString()=="bomb",pad.Name+": left shoulder lifts real bomb");
        CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,false);await ControllerFrames(2);Check(CtlPlayer(0).GetProperty("carrying").ValueKind==JsonValueKind.Null,pad.Name+": release throws bomb");
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3),pad.Name+": thrown bomb enters flight");
        await Capture("controllers-bomb-"+pad.RuntimeId);CtlReleaseAll();_root.Back();await ControllerFrames(3);
        await ControllerPrimaryHoldEvents(pad);
        if(family==ControllerFamily.Nintendo)await ControllerNintendoCharge(pad);
    }
    private async Task ControllerNintendoCharge(GamepadDevice pad)
    {
        CtlReleaseAll();CtlButton(pad.RuntimeId,JoyButton.A,true); // Printed B.
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Switch B alias trial reaches fight",150);
        Check(State.GetProperty("bombs").GetArrayLength()==0,"held Switch B must be released before gameplay arms");
        CtlButton(pad.RuntimeId,JoyButton.A,false);await ControllerFrames(4);
        CtlButton(pad.RuntimeId,JoyButton.X,true);await ControllerFrames(2); // Printed Y stays held.
        CtlButton(pad.RuntimeId,JoyButton.A,true);await ControllerFrames(2);
        CtlButton(pad.RuntimeId,JoyButton.A,false);await ControllerFrames(2);
        Check(CtlPlayer(0).GetProperty("carrying").GetString()=="bomb"&&State.GetProperty("bombs").GetArrayLength()==1,
            "Switch Y plus B creates one bomb overhead without releasing Y");
        CtlButton(pad.RuntimeId,JoyButton.A,true);await ControllerFrames(16);
        var intent=PlayerControls.Read(0,1);
        Check(intent.A&&intent.B&&!intent.R,"Switch Y plus B carries and pumps without requesting remote detonation");
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==1,"holding Switch B pumps once without auto-repeat");
        CtlButton(pad.RuntimeId,JoyButton.A,false);await ControllerFrames(2);
        CtlButton(pad.RuntimeId,JoyButton.A,true);CtlButton(pad.RuntimeId,JoyButton.A,false);await ControllerFrames(16);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==2,"sub-frame Switch B tap survives native buffering and pumps again");
        CtlButton(pad.RuntimeId,JoyButton.B,true);await ControllerFrames(2);CtlButton(pad.RuntimeId,JoyButton.B,false);await ControllerFrames(14);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==3,"Switch printed A remains a pump alternative with Y held");
        CtlButton(pad.RuntimeId,JoyButton.Y,true);intent=PlayerControls.Read(0,1);
        Check(intent.R&&!intent.A&&intent.B,"Switch printed X remains remote only while Y holds the bomb");
        CtlButton(pad.RuntimeId,JoyButton.Y,false);await ControllerFrames(2);
        await Capture("controllers-switch-y-b-charge");
        CtlButton(pad.RuntimeId,JoyButton.X,false);await ControllerFrames(4);
        Check(CtlPlayer(0).GetProperty("carrying").ValueKind==JsonValueKind.Null&&State.GetProperty("bombs")[0].GetProperty("state").GetInt32()==3,
            "releasing Switch Y throws the B-charged bomb");
        CtlReleaseAll();_root.Back();await ControllerFrames(3);
    }
    private async Task ControllerPrimaryHoldEvents(GamepadDevice pad)
    {
        var family=ControllerProfile.Family(pad.Name);
        var primary=ControllerProfile.BombButton(family);var carry=ControllerProfile.CarryButton(family);
        CtlReleaseAll();await ControllerFocusReady();
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"primary hold/pump/throw trial reaches fight",150);await ControllerFrames(4);
        CtlButton(pad.RuntimeId,primary,true);await ControllerFrames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1&&CtlPlayer(0).GetProperty("carrying").ValueKind==JsonValueKind.Null,pad.Name+": primary press first places a ground bomb before hold threshold");
        string bombId=State.GetProperty("bombs")[0].GetProperty("id").GetString()!;
        await ControllerFrames(8);
        Check(CtlPlayer(0).GetProperty("carrying").GetString()=="bomb"&&CtlPlayer(0).GetProperty("carryFromPrimary").GetBoolean(),pad.Name+": sustained primary lifts its own bomb after the hold threshold");
        Check(State.GetProperty("bombs").GetArrayLength()==1&&State.GetProperty("bombs")[0].GetProperty("id").GetString()==bombId,pad.Name+": primary hold carries the same bomb without another placement");
        CtlButton(pad.RuntimeId,primary,false);await ControllerFrames(2);
        Check(CtlPlayer(0).GetProperty("carrying").GetString()=="bomb",pad.Name+": releasing primary preserves the carried bomb");
        CtlButton(pad.RuntimeId,primary,true);await ControllerFrames(2);CtlButton(pad.RuntimeId,primary,false);await ControllerFrames(14);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==1,pad.Name+": fresh primary press pumps the carried bomb once");
        CtlButton(pad.RuntimeId,carry,true);await ControllerFrames(2);
        Check(CtlPlayer(0).GetProperty("carrying").ValueKind==JsonValueKind.Null&&State.GetProperty("bombs")[0].GetProperty("state").GetInt32()==3,pad.Name+": printed carry button throws the primary-held bomb on press");
        CtlButton(pad.RuntimeId,carry,false);CtlReleaseAll();_root.Back();await ControllerFrames(3);
    }
    private async Task ControllerKeyboardSeats()
    {
        CtlReleaseAll();ControllerHub.Current.OverrideDevicesForPlaytest([]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),2,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"two keyboard seats reach fight",150);await ControllerFrames(4);
        float first=CtlPlayer(0).GetProperty("x").GetSingle(),second=CtlPlayer(1).GetProperty("x").GetSingle();CtlKey(Key.D,true);CtlKey(Key.Left,true);await ControllerFrames(8);CtlKey(Key.D,false);CtlKey(Key.Left,false);await ControllerFrames(4);
        float p1=CtlPlayer(0).GetProperty("x").GetSingle()-first,p2=second-CtlPlayer(1).GetProperty("x").GetSingle();Check(p1>.6f&&Math.Abs(p1-p2)<.025f,"P1 WASD and P2 arrows share authoritative speed");
        foreach(var entry in new[]{(0,Key.J,0),(0,Key.K,1),(0,Key.L,3),(0,Key.I,2),(1,Key.N,0),(1,Key.M,1),(1,Key.Comma,2),(0,Key.Shift,3),(0,Key.R,2),(1,Key.Enter,0),(1,Key.Ctrl,3),(1,Key.Backspace,2),(1,Key.Kp0,0),(1,Key.Kp1,1),(1,Key.Kp2,2)})
        {
            CtlKey(entry.Item2,true);var actual=PlayerControls.Read(entry.Item1,2);bool active=entry.Item3==0?actual.A:entry.Item3==1?actual.B:entry.Item3==2?actual.R:actual.Aim;Check(active,$"keyboard seat{entry.Item1+1} {entry.Item2} reaches correct action");
            if(entry.Item1==0&&entry.Item2==Key.K)Check(!actual.Aim,"K carries without entering aim");
            if(entry.Item1==0&&entry.Item2==Key.L)Check(!actual.B,"L enters aim without also carrying or throwing");
            var untouched=PlayerControls.Read(1-entry.Item1,2);Check(!untouched.A&&!untouched.B&&!untouched.R&&!untouched.Aim,$"keyboard {entry.Item2} never leaks to other seat");CtlKey(entry.Item2,false);_ = PlayerControls.Read(entry.Item1,2);
        }
        CtlKey(Key.Space,true);var space=PlayerControls.Read(0,2);Check(!space.A&&!space.B&&!space.R&&!space.Aim,"Space has no gameplay action");CtlKey(Key.Space,false);
        CtlKey(Key.J,true);CtlKey(Key.J,false);CtlKey(Key.N,true);CtlKey(Key.N,false);await ControllerFrames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==2,"both compact keyboard bomb taps reach authority independently");await Capture("controllers-two-keyboards");_root.Back();await ControllerFrames(3);
    }
    private async Task ControllerHotplugEvents()
    {
        var hub=ControllerHub.Current;CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[0],QaPads[1]]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),4,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"four-seat hotplug trial reaches fight",150);await ControllerFrames(4);
        Check(hub.Bindings!.DeviceIdForSeat(0)==null&&hub.Bindings.DeviceIdForSeat(1)==null&&hub.Bindings.DeviceIdForSeat(2)==12&&hub.Bindings.DeviceIdForSeat(3)==1001,"four humans reserve keyboard seats and independent pads");
        CtlAxis(12,JoyAxis.LeftX,.75f);Check(PlayerControls.Read(2,4).X>.99f&&PlayerControls.Read(0,4).X==0&&PlayerControls.Read(1,4).X==0,"assigned pad moves only its own seat");CtlAxis(12,JoyAxis.LeftX,0);
        CtlButton(1001,JoyButton.LeftShoulder,true);Check(PlayerControls.Read(3,4).B,"second pad holds carry before unrelated disconnect");
        hub.OverrideDevicesForPlaytest([QaPads[1],QaPads[0]]);Check(hub.Bindings.DeviceIdForSeat(2)==12&&hub.Bindings.DeviceIdForSeat(3)==1001,"enumeration reorder preserves seats");
        hub.OverrideDevicesForPlaytest([QaPads[1]]);Check(hub.Bindings.DeviceIdForSeat(2)==null&&hub.Bindings.DeviceIdForSeat(3)==1001,"disconnect reserves empty seat without shifting another pad");
        Check(PlayerControls.Read(3,4).B,"unrelated disconnect preserves held carry");Check(PlayerControls.Read(2,4)==default,"disconnected seat reads neutral");
        hub.OverrideDevicesForPlaytest([QaPads[1],new(14,QaPads[0].Guid,QaPads[0].Name)]);Check(hub.Bindings.DeviceIdForSeat(2)==14&&hub.Bindings.DeviceIdForSeat(3)==1001,"reconnect restores unique identity with new runtime ID");
        CtlReleaseAll();await Capture("controllers-stable-hotplug");_root.Back();await ControllerFrames(3);
    }
    private async Task ControllerBoundMenuEvents()
    {
        await ControllerFocusReady();
        CtlReleaseAll();var hub=ControllerHub.Current;hub.OverrideDevicesForPlaytest([QaPads[1]]);
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"retained-binding menu trial reaches fight",150);
        var session=_root.ActiveSession;var bindings=hub.Bindings;
        _root.Navigate("Results");await ControllerFrames(3);FindButton("PLAY AGAIN").GrabFocus();await ControllerFrames(1);
        foreach(float value in new[]{.3f,.35f,.42f,.48f,.65f}){CtlAxis(1001,JoyAxis.LeftX,value);await ControllerFrames(1);}
        Check(FindButton("MODE SELECT").HasFocus(),"results analog navigation works while match controller bindings remain attached");
        CtlAxis(1001,JoyAxis.LeftX,0);await ControllerFrames(2);
        Check(ReferenceEquals(session,_root.ActiveSession)&&ReferenceEquals(bindings,hub.Bindings),"results navigation preserves session and controller assignments");
        _root.Back();await ControllerFrames(2);hub.BeginMatch(1);bindings=hub.Bindings;
        _root.Navigate("Lobby");await ControllerFrames(3);
        var probe=new ControllerMenuProbe();AddChild(probe);
        CtlAxis(1001,JoyAxis.LeftY,.65f);await ControllerFrames(2);
        Check(probe.DownSteps==1&&NamedButton("LobbySettings").HasFocus(),"lobby analog navigation reaches Settings while match bindings remain attached");
        await ControllerFrames(14);Check(probe.DownSteps>=2,"lobby held analog navigation repeats while match bindings remain attached");probe.QueueFree();
        CtlAxis(1001,JoyAxis.LeftY,0);await ControllerFrames(2);
        Check(ReferenceEquals(bindings,hub.Bindings),"lobby navigation preserves controller assignments");
        _root.Back();await ControllerFrames(3);
    }
    private async Task ControllerFocusEvents()
    {
        var hub=ControllerHub.Current;CtlReleaseAll();hub.OverrideDevicesForPlaytest([QaPads[0]]);_root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"focus trial reaches fight",150);await ControllerFrames(4);CtlKey(Key.D,true);CtlKey(Key.J,true);
        hub.Notification((int)Node.NotificationApplicationFocusOut);Check(PlayerControls.Read(0,1)==default,"focus loss returns neutral movement and actions");
        hub.Notification((int)Node.NotificationApplicationFocusIn);Check(!PlayerControls.Read(0,1).A,"held bomb cannot trigger on focus restoration");
        CtlReleaseAll();_ = PlayerControls.Read(0,1);CtlKey(Key.J,true);Check(PlayerControls.Read(0,1).A,"fresh press rearms after focus restoration");CtlReleaseAll();
        foreach(var pad in QaPads)
        {
            hub.OverrideDevicesForPlaytest([pad]);hub.BeginMatch(1);_ = PlayerControls.Read(0,1);
            var primary=ControllerProfile.BombButton(ControllerProfile.Family(pad.Name));
            CtlAxis(pad.RuntimeId,JoyAxis.LeftX,.3f);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,.4f);CtlButton(pad.RuntimeId,primary,true);
            hub.Notification((int)Node.NotificationApplicationFocusOut);Check(PlayerControls.Read(0,1)==default,pad.Name+": focus loss suppresses held stick and primary");
            hub.Notification((int)Node.NotificationApplicationFocusIn);var restored=PlayerControls.Read(0,1);
            Check(!restored.A&&Math.Abs(restored.X-.6f)<.001f&&Math.Abs(restored.Z-.8f)<.001f,pad.Name+": focus restoration preserves continuous movement while requiring primary release");
            CtlButton(pad.RuntimeId,primary,false);_ = PlayerControls.Read(0,1);CtlButton(pad.RuntimeId,primary,true);
            Check(PlayerControls.Read(0,1).A,pad.Name+": fresh primary press rearms after focus restoration");CtlReleaseAll();
        }
        _root.Back();await ControllerFrames(3);
    }
    private JsonElement CtlPlayer(int slot)=>State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("slot").GetInt32()==slot);
    private static void CtlKey(Key key,bool pressed){EngineInput.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed,Echo=false});EngineInput.FlushBufferedEvents();}
    private static void CtlButton(int device,JoyButton button,bool pressed){if(device>=1000){ControllerHub.Current.NativeEventForPlaytest(device,null,0,button,pressed);return;}EngineInput.ParseInputEvent(new InputEventJoypadButton {Device=device,ButtonIndex=button,Pressed=pressed});EngineInput.FlushBufferedEvents();}
    private static void CtlAxis(int device,JoyAxis axis,float value){if(device>=1000){ControllerHub.Current.NativeEventForPlaytest(device,axis,value,null,false);return;}EngineInput.ParseInputEvent(new InputEventJoypadMotion {Device=device,Axis=axis,AxisValue=value});EngineInput.FlushBufferedEvents();}
    private static void CtlReleaseAll()
    {
        foreach(var key in QaKeys)CtlKey(key,false);
        foreach(var pad in QaPads){CtlAxis(pad.RuntimeId,JoyAxis.LeftX,0);CtlAxis(pad.RuntimeId,JoyAxis.LeftY,0);CtlAxis(pad.RuntimeId,JoyAxis.TriggerRight,0);foreach(var button in QaButtons)CtlButton(pad.RuntimeId,button,false);}
    }
}

// Observe real dispatched GUI actions rather than calling the navigation policy directly.
