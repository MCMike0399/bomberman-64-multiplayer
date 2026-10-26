using Godot;
using Bomber.Client.Controls;
using Bomber.Game.Matches;
namespace BlastCircuit.InputDevices;

public sealed record ControllerDevice(GamepadDevice Device,ControllerFamily Family,bool Mapped,string Backend);

/// <summary>Owns device discovery, match-stable assignments, input edges, and focus safety.</summary>
public partial class ControllerHub : Node
{
    public static ControllerHub Current {get;private set;}=null!;
    public event Action? DevicesChanged;
    public event Action? PromptChanged;
    public IReadOnlyList<ControllerDevice> Devices=>_devices;
    public int PlayableCount=>_devices.Count(d=>d.Mapped);
    public MatchControllerBindings? Bindings {get;private set;}
    public ControllerFamily ActiveFamily {get;private set;}=ControllerFamily.Keyboard;
    public int ActiveKeyboardSeat {get;private set;}
    public bool TesterActive {get;set;}
    public bool MenuNavigationEnabled {get;set;}=true;
    public bool HasApplicationFocus=>_focused;
    public event Action<int,string,float>? ObservedInput;
    public event Action<int,JoyAxis,float>? ObservedAxis;
    public string BackendStatus {get;private set;}="Godot controller backend";
    private List<ControllerDevice> _devices=[];
    private IReadOnlyList<GamepadDevice>? _testDevices;
    private readonly bool[] _keyboardBomb=new bool[2],_keyboardRemote=new bool[2],_keyboardStop=new bool[2],_armed=new bool[8];
    private readonly Dictionary<int,(bool Bomb,bool Remote,bool Stop)> _taps=[];
    private bool _focused=true,_inMatch,_vibrationEnabled;
    public int VibrationRequestCount {get;private set;}
    public bool VibrationEnabled {get=>_vibrationEnabled;set{_vibrationEnabled=value;if(!value)StopVibration();}}
    public void RearmControls(){ClearEdges();Array.Fill(_armed,false);}
    public bool VibrateSeat(int seat,float weak,float strong,float seconds)=>Bindings?.DeviceIdForSeat(seat) is {} id && Vibrate(id,weak,strong,seconds);
    public bool TestVibration()=>_devices.FirstOrDefault(d=>d.Mapped) is {} pad && Vibrate(pad.Device.RuntimeId,.35f,.5f,.2f);
    private bool Vibrate(int id,float weak,float strong,float seconds)
    {
        if(!_vibrationEnabled||!_focused||seconds<=0)return false;
        weak=Math.Clamp(weak,0,1);strong=Math.Clamp(strong,0,1);seconds=Math.Clamp(seconds,0,.5f);
        if(_testDevices!=null){VibrationRequestCount++;return true;}
        if(id>=1000){bool ok=_native?.Rumble(id-1000,strong,weak,(uint)(seconds*1000))??false;if(ok)VibrationRequestCount++;return ok;}
        if(!Input.HasJoyVibration(id))return false;
        Input.StartJoyVibration(id,weak,strong,seconds);VibrationRequestCount++;return true;
    }
    public void StopVibration()
    {
        foreach(var pad in _devices)
        {int id=pad.Device.RuntimeId;if(id>=1000)_native?.Rumble(id-1000,0,0,0);else Input.StopJoyVibration(id);}
    }
    private double _refreshClock;
    private readonly Dictionary<int,MenuStickNavigation> _menuSticks=[];
    private readonly Dictionary<int,MenuStickNavigation> _menuDpads=[];
    private readonly Dictionary<int,JoyButton> _menuDpadLast=[];
    private SdlGamepadBackend? _native;
    private readonly Dictionary<(int Device,int Button),bool> _nativeButtons=[];
    private readonly Dictionary<(int Device,int Axis),float> _nativeAxes=[];
    public override void _Ready()
    {
        Current=this;
        ConfigureMenuActions();
        Input.JoyConnectionChanged+=OnConnection;
        if(OperatingSystem.IsMacOS()&&DisplayServer.GetName()!="headless")
        {
            // An exported app loads real adjacent dylibs, never a source-tree or PCK path.
            string defaultLibrary=OS.HasFeature("editor")
                ?ProjectSettings.GlobalizePath("res://native/macos/libSDL3.dylib")
                :Path.GetFullPath(Path.Combine(OS.GetExecutablePath().GetBaseDir(),"../Frameworks/Controllers/libSDL3.dylib"));
            string path=System.Environment.GetEnvironmentVariable("BOMBER_SDL3_LIBRARY")??defaultLibrary;
            if(File.Exists(path))
            {
                _native=SdlGamepadBackend.TryCreate(out var error,path);
                BackendStatus=_native!=null?"Switch 2 USB support ready":"Switch 2 USB support unavailable";
                if(error!=null)GD.PushWarning("Optional Switch 2 USB backend: "+error);
            }
            else BackendStatus="Switch 2 USB support not installed • see setup guide";
        }
        RefreshDevices();
    }
    private static void ConfigureMenuActions()
    {
        static void Add(string action,InputEvent input){if(!InputMap.ActionHasEvent(action,input))InputMap.ActionAddEvent(action,input);}
        foreach(var (action,key) in new[]{("ui_up",Key.W),("ui_left",Key.A),("ui_down",Key.S),("ui_right",Key.D)})
            Add(action,new InputEventKey {PhysicalKeycode=key});
        // Face buttons need per-device semantics (Nintendo printed A/B differs from Xbox).
        // Install exact device bindings on discovery, including embedded PopupMenus.
        foreach(string action in new[]{"ui_accept","ui_cancel"})
            foreach(var input in InputMap.ActionGetEvents(action).OfType<InputEventJoypadButton>())
                InputMap.ActionEraseEvent(action,input);
        foreach(var (action,button,axis,value) in new[]{
            ("ui_left",JoyButton.DpadLeft,JoyAxis.LeftX,-1f),("ui_right",JoyButton.DpadRight,JoyAxis.LeftX,1f),
            ("ui_up",JoyButton.DpadUp,JoyAxis.LeftY,-1f),("ui_down",JoyButton.DpadDown,JoyAxis.LeftY,1f)})
        {
            InputMap.ActionSetDeadzone(action,.45f);
            Add(action,new InputEventJoypadButton {Device=-1,ButtonIndex=button});
            Add(action,new InputEventJoypadMotion {Device=-1,Axis=axis,AxisValue=value});
        }
    }
    public void BeginMatch(int humanCount)
    {
        RefreshDevices();Bindings=new(humanCount,_devices.Where(d=>d.Mapped).Select(d=>d.Device).ToArray());_inMatch=true;MenuNavigationEnabled=false;ClearEdges();
        Array.Fill(_armed,false);
    }
    public void EndMatch(){StopVibration();_inMatch=false;MenuNavigationEnabled=true;Bindings=null;ClearEdges();}
    public PlayerInput Read(int seat)
    {
        if(!_focused||!_inMatch||Bindings==null)return default;
        var keyboard=PlayerControls.Keyboard(seat);
        int? id=Bindings.DeviceIdForSeat(seat);
        PlayerInput stick=default,dpad=default,aimStick=default;
        if(id is {} pad)
        {
            var family=DeviceFamily(pad);
            stick=new(Input.GetJoyAxis(pad,JoyAxis.LeftX),Input.GetJoyAxis(pad,JoyAxis.LeftY),
                Button(pad,ControllerProfile.BombButton(family))||(ControllerProfile.AlternateBombButton(family) is {} alternate&&Button(pad,alternate)),
                Button(pad,ControllerProfile.CarryButton(family))||Button(pad,JoyButton.LeftShoulder),
                Button(pad,ControllerProfile.RemoteButton(family)),
                Input.GetJoyAxis(pad,JoyAxis.TriggerRight)>.35f,Button(pad,JoyButton.RightShoulder));
            dpad=new((Button(pad,JoyButton.DpadRight)?1:0)-(Button(pad,JoyButton.DpadLeft)?1:0),
                (Button(pad,JoyButton.DpadDown)?1:0)-(Button(pad,JoyButton.DpadUp)?1:0));
            aimStick=new(Input.GetJoyAxis(pad,JoyAxis.RightX),Input.GetJoyAxis(pad,JoyAxis.RightY));
        }
        // Release menu-confirm before it can drop a bomb in the newly entered match.
        if(!_armed[seat])
        {
            _armed[seat]=!keyboard.A&&!keyboard.B&&!keyboard.R&&!keyboard.Aim&&!keyboard.Stop&&!stick.A&&!stick.B&&!stick.R&&!stick.Aim&&!stick.Stop;
            keyboard=keyboard with {A=false,B=false,R=false,Aim=false,Stop=false};stick=stick with {A=false,B=false,R=false,Aim=false,Stop=false};
            if(seat<2){_keyboardBomb[seat]=false;_keyboardRemote[seat]=false;_keyboardStop[seat]=false;}
            if(id is {} pending)_taps.Remove(pending);
        }
        else
        {
            if(seat<2)
            {
                keyboard=keyboard with {A=keyboard.A||_keyboardBomb[seat],R=keyboard.R||_keyboardRemote[seat],Stop=keyboard.Stop||_keyboardStop[seat]};
                _keyboardBomb[seat]=false;_keyboardRemote[seat]=false;_keyboardStop[seat]=false;
            }
            if(id is {} device&&_taps.Remove(device,out var taps))
                stick=stick with {A=stick.A||taps.Bomb,R=stick.R||taps.Remote,Stop=stick.Stop||taps.Stop};
        }
        return InputPolicy.Resolve(keyboard,stick,dpad,aimStick);
    }
    private static bool Button(int id,JoyButton button)=>Input.IsJoyButtonPressed(id,button);
    private void OnConnection(long id,bool connected)=>RefreshDevices();
    public void RefreshDevices()
    {
        var next=_testDevices?.Select(d=>new ControllerDevice(d,ControllerProfile.Family(d.Name),true,"Synthetic QA")).ToList()
            ??Input.GetConnectedJoypads().Select(DescribeGodotDevice).ToList();
        if(_testDevices==null&&_native!=null)
            next.AddRange(_native.Devices.Select(d=>new ControllerDevice(new(NativeId(d.Id),d.Guid,d.Name),ControllerFamily.Nintendo,true,"USB")));
        if(_devices.SequenceEqual(next))return;
        var oldIds=_devices.Select(d=>d.Device.RuntimeId).ToHashSet();
        var oldBindings=Bindings?.Seats.Select(s=>s.RuntimeId).ToArray();
        _devices=next;ConfigureDeviceMenuActions();Bindings?.Refresh(next.Where(d=>d.Mapped).Select(d=>d.Device).ToArray());
        foreach(var id in oldIds.Except(next.Select(d=>d.Device.RuntimeId)))_taps.Remove(id);
        if(oldBindings!=null&&Bindings!=null)
            for(int seat=0;seat<oldBindings.Length;seat++)
                if(oldBindings[seat]!=Bindings.DeviceIdForSeat(seat))_armed[seat]=false;
        DevicesChanged?.Invoke();
    }
    private void ConfigureDeviceMenuActions()
    {
        foreach(string action in new[]{"ui_accept","ui_cancel"})
            foreach(var input in InputMap.ActionGetEvents(action).OfType<InputEventJoypadButton>())
                InputMap.ActionEraseEvent(action,input);
        foreach(var device in _devices.Where(d=>d.Mapped))
        {
            InputMap.ActionAddEvent("ui_accept",new InputEventJoypadButton {Device=device.Device.RuntimeId,ButtonIndex=ControllerProfile.ConfirmButton(device.Family)});
            InputMap.ActionAddEvent("ui_cancel",new InputEventJoypadButton {Device=device.Device.RuntimeId,ButtonIndex=ControllerProfile.CancelButton(device.Family)});
        }
    }
    private static ControllerDevice DescribeGodotDevice(int id)
    {
        var info=Input.GetJoyInfo(id);
        int vendor=info.TryGetValue("vendor_id",out var value)&&int.TryParse(value.ToString(),out int parsed)?parsed:0;
        return new(new(id,Input.GetJoyGuid(id),Input.GetJoyName(id)),ControllerProfile.Family(Input.GetJoyName(id),vendor),Input.IsJoyKnown(id),"Godot");
    }
    public override void _Process(double delta)
    {
        PollNative();
        PollMenuSticks(delta);
        _refreshClock+=delta;
        if(_refreshClock>=.5){_refreshClock=0;RefreshDevices();}
    }
    private void PollMenuSticks(double delta)
    {
        if(!_focused||!MenuNavigationEnabled||TesterActive){_menuSticks.Clear();_menuDpads.Clear();_menuDpadLast.Clear();return;}
        foreach(var device in _devices.Where(d=>d.Mapped))
        {
            int id=device.Device.RuntimeId;
            if(!_menuSticks.TryGetValue(id,out var navigation))_menuSticks[id]=navigation=new();
            var dpad=ReadMenuDpad(id);
            int direction;
            if(dpad!=Vector2.Zero)
            {
                navigation.Reset();
                if(!_menuDpads.TryGetValue(id,out var repeat))_menuDpads[id]=repeat=new();
                direction=repeat.Step(dpad.X,dpad.Y,delta);
            }
            else
            {
                if(_menuDpads.TryGetValue(id,out var repeat))repeat.Reset();
                direction=navigation.Step(Input.GetJoyAxis(id,JoyAxis.LeftX),Input.GetJoyAxis(id,JoyAxis.LeftY),delta);
            }
            if(direction==0)continue;
            string action=direction switch {1=>"ui_left",2=>"ui_right",3=>"ui_up",_=>"ui_down"};
            Input.ParseInputEvent(new InputEventAction {Action=action,Pressed=true,Strength=1});
            Input.ParseInputEvent(new InputEventAction {Action=action,Pressed=false});
        }
        foreach(int removed in _menuSticks.Keys.Except(_devices.Select(d=>d.Device.RuntimeId)).ToArray())
        {_menuSticks.Remove(removed);_menuDpads.Remove(removed);_menuDpadLast.Remove(removed);}
    }
    private static Vector2 DpadVector(JoyButton button)=>button switch
    {JoyButton.DpadLeft=>Vector2.Left,JoyButton.DpadRight=>Vector2.Right,JoyButton.DpadUp=>Vector2.Up,JoyButton.DpadDown=>Vector2.Down,_=>Vector2.Zero};
    private Vector2 ReadMenuDpad(int id)
    {
        if(_menuDpadLast.TryGetValue(id,out var last)&&Button(id,last))return DpadVector(last);
        foreach(var button in new[]{JoyButton.DpadLeft,JoyButton.DpadRight,JoyButton.DpadUp,JoyButton.DpadDown})
            if(Button(id,button))return DpadVector(button);
        return Vector2.Zero;
    }
    private static int NativeId(int id)=>1000+id;
    private void PollNative()
    {
        if(_native==null)return;
        _native.Poll();
        var edges=_native.DrainButtonEdges();
        if(_testDevices!=null)return;
        foreach(var edge in edges)
        {
            if(edge.Button is <0 or >14)continue;
            int id=NativeId(edge.Id);
            PublishNativeButton(id,edge.Button,edge.Pressed);
        }
        var connected=_native.Devices.Select(d=>NativeId(d.Id)).ToHashSet();
        foreach(var pad in _native.Devices)
        {
            int id=NativeId(pad.Id);
            PublishNativeAxes(id,axis=>_native.Axis(pad.Id,axis));
            for(int button=0;button<=14;button++)
            {
                bool value=_native.Button(pad.Id,button);_nativeButtons.TryGetValue((id,button),out bool previous);
                if(value==previous)continue;
                PublishNativeButton(id,button,value);
            }
        }
        foreach(var key in _nativeAxes.Keys.Where(k=>!connected.Contains(k.Device)).ToArray())
        {
            Input.ParseInputEvent(new InputEventJoypadMotion {Device=key.Device,Axis=(JoyAxis)key.Axis,AxisValue=0});_nativeAxes.Remove(key);
        }
        foreach(var key in _nativeButtons.Keys.Where(k=>!connected.Contains(k.Device)).ToArray())
        {
            Input.ParseInputEvent(new InputEventJoypadButton {Device=key.Device,ButtonIndex=(JoyButton)key.Button,Pressed=false});_nativeButtons.Remove(key);
        }
        Input.FlushBufferedEvents();
    }
    // SDL and Godot use the same six canonical axes: both sticks, then L/R triggers.
    // Keep sampling shared with the synthetic backend snapshot regression so a
    // missing production trigger cannot be concealed by direct event injection.
    private void PublishNativeAxes(int id,Func<int,float> sample)
    {
        for(int axis=0;axis<6;axis++)PublishNativeAxis(id,axis,sample(axis));
    }
    internal void NativeAxesForPlaytest(int id,IReadOnlyList<float> snapshot)
    {
        if(!OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest="))||id<1000||
            _testDevices is null||!_testDevices.Any(device=>device.RuntimeId==id)||snapshot.Count!=6)
            throw new InvalidOperationException("Native axis snapshots require a registered playtest device and six canonical axes.");
        PublishNativeAxes(id,axis=>snapshot[axis]);
        Input.FlushBufferedEvents();
    }
    private void PublishNativeAxis(int id,int axis,float value)
    {
        _nativeAxes.TryGetValue((id,axis),out float previous);
        if(Mathf.Abs(value-previous)<.0001f)return;
        _nativeAxes[(id,axis)]=value;
        Input.ParseInputEvent(new InputEventJoypadMotion {Device=id,Axis=(JoyAxis)axis,AxisValue=value});
    }
    private void PublishNativeButton(int id,int button,bool pressed)
    {
        _nativeButtons[(id,button)]=pressed;
        Input.ParseInputEvent(new InputEventJoypadButton {Device=id,ButtonIndex=(JoyButton)button,Pressed=pressed});
    }
    internal void NativeEventForPlaytest(int id,JoyAxis? axis,float value,JoyButton? button,bool pressed)
    {
        if(!OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest="))||id<1000)
            throw new InvalidOperationException("Native event replay is restricted to the playtest harness.");
        if(axis is {} a)PublishNativeAxis(id,(int)a,value);
        if(button is {} b)PublishNativeButton(id,(int)b,pressed);
        Input.FlushBufferedEvents();
    }
    public override void _Input(InputEvent @event)
    {
        if(@event is InputEventKey {Pressed:true,Echo:false} key)
        {
            var pressed=key.PhysicalKeycode==Key.None?key.Keycode:key.PhysicalKeycode;
            int keyboardSeat=pressed is Key.N or Key.M or Key.Comma or Key.Left or Key.Right or Key.Up or Key.Down or Key.Kp0 or Key.Kp1 or Key.Kp2 or Key.Enter or Key.Backspace or Key.Ctrl or Key.Period or Key.Kp3?1:0;
            if(ActiveKeyboardSeat!=keyboardSeat){ActiveKeyboardSeat=keyboardSeat;PromptChanged?.Invoke();}
            SetActiveFamily(ControllerFamily.Keyboard);
            if(_inMatch&&_focused)for(int seat=0;seat<2;seat++)
            {
                var physical=key.PhysicalKeycode==Key.None?key.Keycode:key.PhysicalKeycode;
                _keyboardBomb[seat]|=PlayerControls.BombKey(seat,physical);
                _keyboardRemote[seat]|=PlayerControls.RemoteKey(seat,physical);
                _keyboardStop[seat]|=PlayerControls.StopKey(seat,physical);
            }
        }
        else if(@event is InputEventJoypadButton {Pressed:true} button)
        {
            SetActiveDevice(button.Device);
            ObservedInput?.Invoke(button.Device,"button:"+button.ButtonIndex,1);
            if(_focused&&MenuNavigationEnabled&&!TesterActive&&DpadVector(button.ButtonIndex) is var dpad&&dpad!=Vector2.Zero)
            {
                _menuDpadLast[button.Device]=button.ButtonIndex;
                if(!_menuDpads.TryGetValue(button.Device,out var repeat))_menuDpads[button.Device]=repeat=new();
                // Native GUI receives this press immediately, including sub-frame taps and popups.
                // Prime only the repeat timer so polling cannot count the initial press twice.
                repeat.Reset();repeat.Step(dpad.X,dpad.Y,0);
            }
            if(_inMatch&&_focused)
            {
                _taps.TryGetValue(button.Device,out var old);
                var family=DeviceFamily(button.Device);
                _taps[button.Device]=(old.Bomb||ControllerProfile.IsBombButton(family,button.ButtonIndex),
                    old.Remote||button.ButtonIndex==ControllerProfile.RemoteButton(family),old.Stop||button.ButtonIndex==JoyButton.RightShoulder);
            }
        }
        else if(@event is InputEventJoypadMotion motion)
        {
            ObservedAxis?.Invoke(motion.Device,motion.Axis,motion.AxisValue);
            if(Mathf.Abs(motion.AxisValue)>.35f)
            {SetActiveDevice(motion.Device);ObservedInput?.Invoke(motion.Device,"axis:"+motion.Axis,motion.AxisValue);}
        }
        // Godot GUI's joy-motion navigation uses its own threshold/repeat semantics.
        // One explicit policy handles gradual crossings and stationary native USB reports.
        if(MenuNavigationEnabled&&@event is InputEventJoypadMotion {Axis:JoyAxis.LeftX or JoyAxis.LeftY})
            GetViewport().SetInputAsHandled();
        if(TesterActive&&(@event is InputEventJoypadMotion||@event is InputEventJoypadButton {ButtonIndex:not JoyButton.Start}))
            GetViewport().SetInputAsHandled();
    }
    private ControllerFamily DeviceFamily(int id)=>_devices.FirstOrDefault(d=>d.Device.RuntimeId==id)?.Family??ControllerFamily.Generic;
    private void SetActiveDevice(int id)
    {
        var device=_devices.FirstOrDefault(d=>d.Device.RuntimeId==id);
        if(device!=null)SetActiveFamily(device.Family);
    }
    private void SetActiveFamily(ControllerFamily family)
    {if(ActiveFamily==family)return;ActiveFamily=family;PromptChanged?.Invoke();}
    public override void _Notification(int what)
    {
        if(what==NotificationApplicationFocusOut){_focused=false;StopVibration();ClearEdges();}
        if(what==NotificationApplicationFocusIn){_focused=true;Array.Fill(_armed,false);}
    }
    private void ClearEdges(){Array.Clear(_keyboardBomb);Array.Clear(_keyboardRemote);Array.Clear(_keyboardStop);_taps.Clear();_menuSticks.Clear();_menuDpads.Clear();_menuDpadLast.Clear();}
    internal void OverrideDevicesForPlaytest(IReadOnlyList<GamepadDevice> devices)
    {
        if(!OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest=")))throw new InvalidOperationException("Device substitution is restricted to the playtest harness.");
        _testDevices=devices;RefreshDevices();
    }
    internal void RestorePhysicalDevicesForPlaytest(){_testDevices=null;_nativeButtons.Clear();_nativeAxes.Clear();RefreshDevices();}
    public string BindingLabel(int seat,int humans)
    {
        var playable=_devices.Where(d=>d.Mapped).ToArray();
        int? index=LocalControllerLayout.GamepadIndex(seat,humans,playable.Length);
        var keyboard=seat==0?"WASD + J/K/L":seat==1?"ARROWS + N/M/,":"";
        string pad=index is {} i?playable[i].Device.Name:"";
        return keyboard.Length>0?(pad.Length>0?$"{keyboard}  •  {pad}":keyboard):pad.Length>0?pad:"CONNECT GAMEPAD";
    }
    public override void _ExitTree(){StopVibration();Input.JoyConnectionChanged-=OnConnection;_native?.Dispose();}
}
