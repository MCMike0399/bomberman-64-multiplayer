using Godot;
using System.Globalization;
namespace BlastCircuit.UI.Hud;

/// <summary>Read-only, device-local telemetry. Sampling stops entirely while hidden.</summary>
public partial class PerformanceBar : Control
{
    public const int BarHeight=32;
    private double _sampleSeconds;
    private int _frames;
    public int SampleCount {get;private set;}
    public double Fps {get;private set;}
    public double FrameMilliseconds {get;private set;}
    public double ProcessMilliseconds {get;private set;}
    public double VideoMemoryBytes {get;private set;}
    public long DrawCalls {get;private set;}
    private readonly string[] _captions=["FPS","FRAME","PROCESS","PHYSICS","VRAM","DRAWS","CPU TEMP"];
    private readonly string[] _values=["—","—","—","—","—","—","N/A"];

    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;FocusMode=FocusModeEnum.None;
        AnchorRight=1;OffsetBottom=BarHeight;
        VisibilityChanged+=()=>{_sampleSeconds=0;_frames=0;SetProcess(Visible);};
        SetProcess(Visible);
    }

    public override void _Process(double delta)
    {
        _sampleSeconds+=delta;_frames++;
        if(_sampleSeconds<.5)return;
        // Actual engine frames, rather than a fixed simulation tick or 1 / process time.
        FrameMilliseconds=_sampleSeconds/Math.Max(1,_frames)*1000;
        Fps=Engine.GetFramesPerSecond();
        ProcessMilliseconds=Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000;
        var physics=Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess)*1000;
        VideoMemoryBytes=Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        DrawCalls=(long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _values[0]=Number(Fps,"0");_values[1]=Milliseconds(FrameMilliseconds);
        _values[2]=Milliseconds(ProcessMilliseconds);_values[3]=Milliseconds(physics);
        // Some renderers return zero for unsupported memory counters. Never imply zero usage.
        _values[4]=VideoMemoryBytes>0?Number(VideoMemoryBytes/(1024*1024),"0")+" MiB":"N/A";
        _values[5]=DrawCalls.ToString(CultureInfo.InvariantCulture);
        SampleCount++;_sampleSeconds=0;_frames=0;QueueRedraw();
    }

    private static string Number(double value,string format)=>double.IsFinite(value)&&value>=0?value.ToString(format,CultureInfo.InvariantCulture):"N/A";
    private static string Milliseconds(double value)=>Number(value,"0.0")+" ms";

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color(ArcadeTheme.Night,.94f));
        DrawLine(new(0,BarHeight-1),new(Size.X,BarHeight-1),new Color(ArcadeTheme.Aqua,.6f));
        var font=ArcadeTheme.Body;float cell=(Size.X-90)/_captions.Length;
        int fontSize=cell<150?13:15;
        for(int i=0;i<_captions.Length;i++)
        {
            float x=12+i*cell;
            DrawString(font,new(x,21),_captions[i],fontSize:fontSize,modulate:ArcadeTheme.Muted);
            float labelWidth=font.GetStringSize(_captions[i],fontSize:fontSize).X;
            DrawString(font,new(x+labelWidth+8,21),_values[i],fontSize:fontSize,modulate:i==0?ArcadeTheme.Aqua:ArcadeTheme.Cream);
            if(i>0)DrawLine(new(x-6,9),new(x-6,23),new Color(ArcadeTheme.Muted,.25f));
        }
        DrawString(font,new(Size.X-76,21),"F3 · Hide",fontSize:13,modulate:ArcadeTheme.Muted);
    }
}
