using Godot;
using Bomber.Client;
using Bomber.Game;
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
    public bool HasApplicationFocus=>_focused;
    public event Action<int,string,float>? ObservedInput;
    public string BackendStatus {get;private set;}="Godot controller backend";
    private List<ControllerDevice> _devices=[];
    private IReadOnlyList<GamepadDevice>? _testDevices;
    private readonly bool[] _keyboardBomb=new bool[2],_keyboardRemote=new bool[2],_armed=new bool[8];
    private readonly Dictionary<int,(bool Bomb,bool Remote)> _taps=[];
    private bool _focused=true,_inMatch;
    private double _refreshClock;
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
            string path=System.Environment.GetEnvironmentVariable("BOMBER_SDL3_LIBRARY")??ProjectSettings.GlobalizePath("res://native/macos/libSDL3.dylib");
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
        Add("ui_accept",new InputEventJoypadButton {Device=-1,ButtonIndex=JoyButton.A});
        Add("ui_cancel",new InputEventJoypadButton {Device=-1,ButtonIndex=JoyButton.B});
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
        RefreshDevices();Bindings=new(humanCount,_devices.Where(d=>d.Mapped).Select(d=>d.Device).ToArray());_inMatch=true;ClearEdges();
        Array.Fill(_armed,false);
    }
    public void EndMatch(){_inMatch=false;Bindings=null;ClearEdges();}
    public PlayerInput Read(int seat)
    {
        if(!_focused||!_inMatch||Bindings==null)return default;
        var keyboard=PlayerControls.Keyboard(seat);
        int? id=Bindings.DeviceIdForSeat(seat);
        PlayerInput stick=default,dpad=default;
        if(id is {} pad)
        {
            stick=new(Input.GetJoyAxis(pad,JoyAxis.LeftX),Input.GetJoyAxis(pad,JoyAxis.LeftY),
                Button(pad,JoyButton.A),Button(pad,JoyButton.X)||Button(pad,JoyButton.LeftShoulder),
                Button(pad,JoyButton.B)||Button(pad,JoyButton.RightShoulder));
            dpad=new((Button(pad,JoyButton.DpadRight)?1:0)-(Button(pad,JoyButton.DpadLeft)?1:0),
                (Button(pad,JoyButton.DpadDown)?1:0)-(Button(pad,JoyButton.DpadUp)?1:0));
        }
        // Release menu-confirm before it can drop a bomb in the newly entered match.
        if(!_armed[seat])
        {
            _armed[seat]=!keyboard.A&&!keyboard.B&&!keyboard.R&&!stick.A&&!stick.B&&!stick.R;
            keyboard=keyboard with {A=false,B=false,R=false};stick=stick with {A=false,B=false,R=false};
            if(seat<2){_keyboardBomb[seat]=false;_keyboardRemote[seat]=false;}
            if(id is {} pending)_taps.Remove(pending);
        }
        else
        {
            if(seat<2)
            {
                keyboard=keyboard with {A=keyboard.A||_keyboardBomb[seat],R=keyboard.R||_keyboardRemote[seat]};
                _keyboardBomb[seat]=false;_keyboardRemote[seat]=false;
            }
            if(id is {} device&&_taps.Remove(device,out var taps))
                stick=stick with {A=stick.A||taps.Bomb,R=stick.R||taps.Remote};
        }
        return InputPolicy.Resolve(keyboard,stick,dpad);
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
        _devices=next;Bindings?.Refresh(next.Where(d=>d.Mapped).Select(d=>d.Device).ToArray());
        foreach(var id in oldIds.Except(next.Select(d=>d.Device.RuntimeId)))_taps.Remove(id);
        if(oldBindings!=null&&Bindings!=null)
            for(int seat=0;seat<oldBindings.Length;seat++)
                if(oldBindings[seat]!=Bindings.DeviceIdForSeat(seat))_armed[seat]=false;
        DevicesChanged?.Invoke();
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
        _refreshClock+=delta;
        if(_refreshClock>=.5){_refreshClock=0;RefreshDevices();}
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
            _nativeButtons[(id,edge.Button)]=edge.Pressed;
            Input.ParseInputEvent(new InputEventJoypadButton {Device=id,ButtonIndex=(JoyButton)edge.Button,Pressed=edge.Pressed});
        }
        var connected=_native.Devices.Select(d=>NativeId(d.Id)).ToHashSet();
        foreach(var pad in _native.Devices)
        {
            int id=NativeId(pad.Id);
            for(int axis=0;axis<2;axis++)
            {
                float value=_native.Axis(pad.Id,axis);_nativeAxes.TryGetValue((id,axis),out float previous);
                if(Mathf.Abs(value-previous)<.0001f)continue;
                _nativeAxes[(id,axis)]=value;
                Input.ParseInputEvent(new InputEventJoypadMotion {Device=id,Axis=(JoyAxis)axis,AxisValue=value});
            }
            for(int button=0;button<=14;button++)
            {
                bool value=_native.Button(pad.Id,button);_nativeButtons.TryGetValue((id,button),out bool previous);
                if(value==previous)continue;
                _nativeButtons[(id,button)]=value;
                Input.ParseInputEvent(new InputEventJoypadButton {Device=id,ButtonIndex=(JoyButton)button,Pressed=value});
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
    public override void _Input(InputEvent @event)
    {
        if(@event is InputEventKey {Pressed:true,Echo:false} key)
        {
            var pressed=key.PhysicalKeycode==Key.None?key.Keycode:key.PhysicalKeycode;
            int keyboardSeat=pressed is Key.N or Key.M or Key.Comma or Key.Left or Key.Right or Key.Up or Key.Down or Key.Kp0 or Key.Kp1 or Key.Kp2?1:0;
            if(ActiveKeyboardSeat!=keyboardSeat){ActiveKeyboardSeat=keyboardSeat;PromptChanged?.Invoke();}
            SetActiveFamily(ControllerFamily.Keyboard);
            if(_inMatch&&_focused)for(int seat=0;seat<2;seat++)
            {
                var physical=key.PhysicalKeycode==Key.None?key.Keycode:key.PhysicalKeycode;
                _keyboardBomb[seat]|=PlayerControls.BombKey(seat,physical);
                _keyboardRemote[seat]|=PlayerControls.RemoteKey(seat,physical);
            }
        }
        else if(@event is InputEventJoypadButton {Pressed:true} button)
        {
            SetActiveDevice(button.Device);
            ObservedInput?.Invoke(button.Device,"button:"+button.ButtonIndex,1);
            if(_inMatch&&_focused)
            {
                _taps.TryGetValue(button.Device,out var old);
                _taps[button.Device]=(old.Bomb||button.ButtonIndex==JoyButton.A,
                    old.Remote||button.ButtonIndex is JoyButton.B or JoyButton.RightShoulder);
            }
        }
        else if(@event is InputEventJoypadMotion motion&&Mathf.Abs(motion.AxisValue)>.35f)
        {SetActiveDevice(motion.Device);ObservedInput?.Invoke(motion.Device,"axis:"+motion.Axis,motion.AxisValue);}
        if(TesterActive&&(@event is InputEventJoypadMotion||@event is InputEventJoypadButton {ButtonIndex:not JoyButton.Start}))
            GetViewport().SetInputAsHandled();
    }
    private void SetActiveDevice(int id)
    {
        var device=_devices.FirstOrDefault(d=>d.Device.RuntimeId==id);
        if(device!=null)SetActiveFamily(device.Family);
    }
    private void SetActiveFamily(ControllerFamily family)
    {if(ActiveFamily==family)return;ActiveFamily=family;PromptChanged?.Invoke();}
    public override void _Notification(int what)
    {
        if(what==NotificationApplicationFocusOut){_focused=false;ClearEdges();}
        if(what==NotificationApplicationFocusIn){_focused=true;Array.Fill(_armed,false);}
    }
    private void ClearEdges(){Array.Clear(_keyboardBomb);Array.Clear(_keyboardRemote);_taps.Clear();}
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
    public override void _ExitTree(){Input.JoyConnectionChanged-=OnConnection;_native?.Dispose();}
}
