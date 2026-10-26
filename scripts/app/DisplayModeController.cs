using Godot;
using BlastCircuit.Rendering;
namespace BlastCircuit.App;

/// <summary>Device-local window sizing and fullscreen rendering; never changes the monitor's display mode.</summary>
public partial class DisplayModeController : Node
{
    public static DisplayModeController Current {get;private set;}=null!;
    public static readonly Vector2I[] Resolutions=[Vector2I.Zero,new(1280,720),new(1280,800),new(1600,900),new(1600,1000),
        new(1920,1080),new(1920,1200),new(2560,1440),new(2560,1600),new(3840,2160),new(3840,2400)];
    public static string ResolutionLabel(Vector2I size)=>size==Vector2I.Zero?"Automatic":$"{size.X} x {size.Y}";
    public static Vector2I ValidResolution(Vector2I size)=>Resolutions.Contains(size)?size:Vector2I.Zero;
    public bool Fullscreen {get;private set;}=true;
    public Vector2I Resolution {get;private set;}
    public event Action? Changed;
    private Window.ModeEnum _requested=Window.ModeEnum.Windowed;
    private ulong _settleUntil,_nextAttempt;
    private bool _resizePending;

    public override void _Ready()
    {
        Current=this;SetProcess(false);
        GetWindow().SizeChanged+=ApplyRenderResolution;
    }

    public void Configure(bool fullscreen,Vector2I resolution,bool startup=false)
    {
        resolution=ValidResolution(resolution);
        bool modeChanged=Fullscreen!=fullscreen;
        bool sizeChanged=Resolution!=resolution;
        Fullscreen=fullscreen;Resolution=resolution;
        if(DisplayServer.GetName()=="headless")return;
        // Automation's initial window override must never rewrite the saved preference.
        bool windowedOverride=startup&&OS.GetCmdlineUserArgs().Contains("--display-windowed");
        if(startup||modeChanged)RequestMode(fullscreen&&!windowedOverride?Window.ModeEnum.Fullscreen:Window.ModeEnum.Windowed);
        else if(sizeChanged&&_requested==Window.ModeEnum.Windowed)ResizeToPreference();
        ApplyRenderResolution();
    }

    private void RequestMode(Window.ModeEnum mode)
    {
        _requested=mode;_settleUntil=Time.GetTicksMsec()+5000;_nextAttempt=0;
        _resizePending=mode==Window.ModeEnum.Windowed;
        GetWindow().Mode=mode;SetProcess(true);
    }

    private void ResizeToPreference()
    {
        var window=GetWindow();
        var usable=DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
        // Leave room for native window decorations; fit a large saved size on a smaller display.
        var available=new Vector2I(Math.Max(1,usable.Size.X-48),Math.Max(1,usable.Size.Y-80));
        var target=Resolution==Vector2I.Zero?new Vector2I(1280,800):Resolution;
        float fit=Math.Min(1,Math.Min((float)available.X/target.X,(float)available.Y/target.Y));
        window.Size=new(Math.Max(1,(int)(target.X*fit)),Math.Max(1,(int)(target.Y*fit)));
        window.Position=usable.Position+(usable.Size-window.Size)/2;
    }

    public void ApplyRenderResolution()
    {
        var window=GetWindow();float scale=1;
        if(window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen&&Resolution!=Vector2I.Zero)
            scale=Math.Min(1,Math.Min((float)Resolution.X/Math.Max(1,window.Size.X),(float)Resolution.Y/Math.Max(1,window.Size.Y)));
        scale*=GraphicsQuality.Current==GraphicsPreset.Low?.85f:1;
        window.Scaling3DScale=Math.Clamp(scale,.1f,1);
        window.Scaling3DMode=scale<1?Viewport.Scaling3DModeEnum.Fsr:Viewport.Scaling3DModeEnum.Bilinear;
    }

    public override void _Process(double delta)
    {
        ulong now=Time.GetTicksMsec();
        if(now>_settleUntil){SetProcess(false);return;}
        if(now<_nextAttempt)return;
        _nextAttempt=now+250;
        // Cocoa fullscreen transitions are asynchronous; retain the latest intent.
        if(GetWindow().Mode!=_requested)GetWindow().Mode=_requested;
        else if(_resizePending){_resizePending=false;ResizeToPreference();}
        ApplyRenderResolution();
    }

    public override void _Input(InputEvent @event)
    {
        if(@event is not InputEventKey {Pressed:true,Echo:false} key)return;
        if(key.Keycode!=Key.F11 && !(key.Keycode==Key.Enter&&key.AltPressed))return;
        if(Time.GetTicksMsec()>_settleUntil)_requested=GetWindow().Mode;
        Fullscreen=_requested is not (Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen);
        RequestMode(Fullscreen?Window.ModeEnum.Fullscreen:Window.ModeEnum.Windowed);
        Changed?.Invoke();GetViewport().SetInputAsHandled();
    }
}
