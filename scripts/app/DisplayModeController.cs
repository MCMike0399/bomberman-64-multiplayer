using Godot;
namespace BlastCircuit.App;

/// <summary>Fullscreen by default, with global shortcuts and an explicit launch override for automation.</summary>
public partial class DisplayModeController : Node
{
    private Window.ModeEnum _requested=Window.ModeEnum.Windowed;
    private ulong _settleUntil,_nextAttempt;
    public override async void _Ready()
    {
        // Apply after native-window creation: entering fullscreen in project settings and
        // immediately leaving it races macOS's asynchronous launch transition.
        if(OS.GetCmdlineUserArgs().Contains("--display-windowed"))return;
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        RequestMode(Window.ModeEnum.Fullscreen);
    }
    private void RequestMode(Window.ModeEnum mode)
    {
        _requested=mode;_settleUntil=Time.GetTicksMsec()+5000;_nextAttempt=0;
        GetWindow().Mode=mode;SetProcess(true);
    }
    public override void _Process(double delta)
    {
        ulong now=Time.GetTicksMsec();
        if(now>_settleUntil){SetProcess(false);return;}
        if(now<_nextAttempt)return;
        _nextAttempt=now+250;
        // Native fullscreen transitions may complete after a second toggle; keep the latest intent.
        if(GetWindow().Mode!=_requested)GetWindow().Mode=_requested;
    }
    public override void _Input(InputEvent @event)
    {
        if(@event is not InputEventKey {Pressed:true,Echo:false} key)return;
        if(key.Keycode!=Key.F11 && !(key.Keycode==Key.Enter&&key.AltPressed))return;
        if(Time.GetTicksMsec()>_settleUntil)_requested=GetWindow().Mode;
        RequestMode(_requested is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen
            ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen);
        GetViewport().SetInputAsHandled();
    }
}
