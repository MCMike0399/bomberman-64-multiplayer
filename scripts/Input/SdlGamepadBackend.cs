using System.Runtime.InteropServices;
namespace BlastCircuit.InputDevices;

public sealed record NativePad(int Id,string Name,string Guid);
public readonly record struct NativeButtonEdge(int Id,int Button,bool Pressed);

/// <summary>Optional Switch 2 USB adapter. Its private SDL instance never opens other controller families.</summary>
public sealed class SdlGamepadBackend : IDisposable
{
    private const uint InitFlags=0x2000|0x4000;
    private readonly Api _api;
    private readonly Dictionary<int,(NativePad Info,nint Handle)> _pads=[];
    private readonly Queue<NativeButtonEdge> _edges=[];
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly nint _event=Marshal.AllocHGlobal(128);
    private bool _disposed;
    public IReadOnlyList<NativePad> Devices {get;private set;}=[];
    private SdlGamepadBackend(Api api){_api=api;}
    public static SdlGamepadBackend? TryCreate(out string? error,string path)
    {
        error=null;Api? api=null;SdlGamepadBackend? backend=null;
        try
        {
            api=new Api(Path.GetFullPath(path));
            if(api.Version()<3004000)throw new InvalidOperationException("Switch 2 USB requires SDL 3.4 or later.");
            api.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS","1");
            api.SetHint("SDL_JOYSTICK_HIDAPI","1");
            api.SetHint("SDL_HIDAPI_LIBUSB","1");
            api.SetHint("SDL_JOYSTICK_HIDAPI_SWITCH2","1");
            api.SetHint("SDL_GAMECONTROLLER_IGNORE_DEVICES_EXCEPT","0x057e/0x2069");
            if(!api.Init(InitFlags))throw new InvalidOperationException(api.Error());
            backend=new(api);backend.Poll();return backend;
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            if(backend!=null)backend.Dispose();else if(api!=null){api.Quit(InitFlags);api.Dispose();}
            error=ex.Message;return null;
        }
    }
    private void ThreadCheck()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("SDL controller access must stay on its owning engine thread.");
    }
    public void Poll()
    {
        ThreadCheck();_api.Pump();
        var seen=new HashSet<int>();
        nint ids=_api.GetGamepads(out int count);
        try
        {
            for(int i=0;i<count;i++)
            {
                uint raw=unchecked((uint)Marshal.ReadInt32(ids,i*4));int id=checked((int)raw);
                if(_api.Vendor(raw)!=0x057e||_api.Product(raw)!=0x2069)continue;
                seen.Add(id);if(_pads.ContainsKey(id))continue;
                var handle=_api.Open(raw);if(handle==0)continue;
                const string name="Switch 2 Pro Controller";
                var guid=_api.Guid(raw);nint text=Marshal.AllocHGlobal(33);
                string value;
                try{_api.GuidString(guid,text,33);value=Marshal.PtrToStringUTF8(text)??"";}
                finally{Marshal.FreeHGlobal(text);}
                _pads[id]=(new(id,name,value),handle);
            }
        }
        finally{if(ids!=0)_api.Free(ids);}
        foreach(int removed in _pads.Keys.Where(id=>!seen.Contains(id)).ToArray())
        {_api.Close(_pads[removed].Handle);_pads.Remove(removed);}
        Devices=_pads.Values.Select(p=>p.Info).OrderBy(p=>p.Id).ToArray();
        _api.Update();
        while(_api.PollEvent(_event))
        {
            uint type=unchecked((uint)Marshal.ReadInt32(_event));
            if(type is not (0x651 or 0x652))continue;
            int id=Marshal.ReadInt32(_event,16);if(!_pads.ContainsKey(id))continue;
            if(_edges.Count>=256)_edges.Dequeue();
            _edges.Enqueue(new(id,Marshal.ReadByte(_event,20),type==0x651));
        }
    }
    public float Axis(int id,int axis)
    {
        ThreadCheck();if(axis is <0 or >5||!_pads.TryGetValue(id,out var pad))return 0;
        short raw=_api.Axis(pad.Handle,axis);return raw<0?raw/32768f:raw/32767f;
    }
    public bool Button(int id,int button)
    {ThreadCheck();return button>=0&&button<32&&_pads.TryGetValue(id,out var p)&&_api.Button(p.Handle,button);}
    public IReadOnlyList<NativeButtonEdge> DrainButtonEdges()
    {ThreadCheck();var value=_edges.ToArray();_edges.Clear();return value;}
    public bool Rumble(int id,float low,float high,uint milliseconds)
    {
        ThreadCheck();return _pads.TryGetValue(id,out var pad)&&_api.Rumble(pad.Handle,(ushort)(Math.Clamp(low,0,1)*65535),(ushort)(Math.Clamp(high,0,1)*65535),milliseconds);
    }
    public void Dispose()
    {
        if(_disposed)return;ThreadCheck();
        foreach(var p in _pads.Values)_api.Close(p.Handle);
        _pads.Clear();Devices=[];_edges.Clear();Marshal.FreeHGlobal(_event);
        _api.Quit(InitFlags);_api.Dispose();_disposed=true;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct Guid128 {public readonly ulong A,B;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool HintFn([MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool InitFn(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FlagsFn(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VersionFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint IdsFn(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint OpenFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseFn(nint pad);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint NameFn(nint pad);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ushort IdentFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate Guid128 GuidFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GuidTextFn(Guid128 guid,nint text,int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short AxisFn(nint pad,int axis);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool ButtonFn(nint pad,int button);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeFn(nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool PollFn(nint evt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool RumbleFn(nint pad,ushort low,ushort high,uint milliseconds);
    private sealed class Api : IDisposable
    {
        private nint _library;
        public readonly HintFn SetHint;public readonly InitFn Init;public readonly FlagsFn Quit;
        public readonly VoidFn Pump,Update;public readonly VersionFn Version;public readonly IdsFn GetGamepads;public readonly OpenFn Open;
        public readonly RumbleFn Rumble;
        public readonly CloseFn Close;public readonly NameFn Name;public readonly IdentFn Vendor,Product;
        public readonly GuidFn Guid;public readonly GuidTextFn GuidString;public readonly AxisFn Axis;
        public readonly ButtonFn Button;public readonly FreeFn Free;public readonly PollFn PollEvent;
        private readonly ErrorFn _error;
        public string Error()=>Marshal.PtrToStringUTF8(_error())??"SDL controller initialization failed.";
        public Api(string path)
        {
            _library=NativeLibrary.Load(path);
            T Bind<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library,name));
            try
            {
                Rumble=Bind<RumbleFn>("SDL_RumbleGamepad");
                SetHint=Bind<HintFn>("SDL_SetHint");Init=Bind<InitFn>("SDL_InitSubSystem");Quit=Bind<FlagsFn>("SDL_QuitSubSystem");
                Pump=Bind<VoidFn>("SDL_PumpEvents");Update=Bind<VoidFn>("SDL_UpdateGamepads");Version=Bind<VersionFn>("SDL_GetVersion");GetGamepads=Bind<IdsFn>("SDL_GetGamepads");
                Open=Bind<OpenFn>("SDL_OpenGamepad");Close=Bind<CloseFn>("SDL_CloseGamepad");Name=Bind<NameFn>("SDL_GetGamepadName");
                Vendor=Bind<IdentFn>("SDL_GetGamepadVendorForID");Product=Bind<IdentFn>("SDL_GetGamepadProductForID");
                Guid=Bind<GuidFn>("SDL_GetGamepadGUIDForID");GuidString=Bind<GuidTextFn>("SDL_GUIDToString");
                Axis=Bind<AxisFn>("SDL_GetGamepadAxis");Button=Bind<ButtonFn>("SDL_GetGamepadButton");Free=Bind<FreeFn>("SDL_free");
                PollEvent=Bind<PollFn>("SDL_PollEvent");_error=Bind<ErrorFn>("SDL_GetError");
            }
            catch{Dispose();throw;}
        }
        public void Dispose(){if(_library!=0){NativeLibrary.Free(_library);_library=0;}}
    }
}
