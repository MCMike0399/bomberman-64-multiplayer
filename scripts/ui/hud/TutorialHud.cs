using Godot;
using System.Text.Json;
using Bomber.Game;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI.Hud;

/// <summary>A quiet how-to overlay; authority reports progress and the player chooses when to continue.</summary>
public partial class TutorialHud : Control
{
    public event Action? RetryRequested,NextRequested,LessonsRequested;
    private Label _title=null!,_objective=null!,_instructions=null!,_tip=null!;
    private HBoxContainer _actions=null!;
    private Button _next=null!,_retry=null!;
    private TutorialLesson? _lesson;
    private bool _finished;
    public bool Finished=>_finished;
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);MouseFilter=MouseFilterEnum.Ignore;
        var top=new PanelContainer {Name="TutorialHeader",AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=-580,OffsetRight=580,OffsetTop=16,OffsetBottom=114,MouseFilter=MouseFilterEnum.Ignore};
        top.AddThemeStyleboxOverride("panel",ArcadeTheme.Plate(14));AddChild(top);
        var heading=ArcadeTheme.Column(top,3);
        _title=ArcadeTheme.Heading(heading,"",27);_title.HorizontalAlignment=HorizontalAlignment.Center;
        _objective=ArcadeTheme.Text(heading,"",20,ArcadeTheme.Gold);_objective.HorizontalAlignment=HorizontalAlignment.Center;
        var bottom=new PanelContainer {Name="TutorialInstructions",AnchorLeft=.5f,AnchorRight=.5f,AnchorTop=1,AnchorBottom=1,OffsetLeft=-620,OffsetRight=620,OffsetTop=-190,OffsetBottom=-20,GrowVertical=GrowDirection.Begin};
        bottom.AddThemeStyleboxOverride("panel",ArcadeTheme.Plate(18));AddChild(bottom);
        var body=ArcadeTheme.Column(bottom,8);
        _instructions=ArcadeTheme.Text(body,"",21);_instructions.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _tip=ArcadeTheme.Text(body,"",16,ArcadeTheme.Muted);_tip.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _actions=ArcadeTheme.Row(body,18);
        _retry=ArcadeTheme.Button(_actions,"Retry",()=>RetryRequested?.Invoke());_retry.Name="TutorialRetry";
        var lessons=ArcadeTheme.Button(_actions,"Lessons",()=>LessonsRequested?.Invoke());lessons.Name="TutorialLessonsBack";
        _actions.AddChild(new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill});
        _next=ArcadeTheme.Button(_actions,"Next lesson",()=>NextRequested?.Invoke());_next.Name="TutorialNext";
        foreach(var b in new[]{_retry,lessons,_next}){b.CustomMinimumSize=new(190,42);b.AddThemeFontSizeOverride("font_size",17);}
        ControllerHub.Current.PromptChanged+=Refresh;
    }
    public override void _ExitTree()=>ControllerHub.Current.PromptChanged-=Refresh;
    public void Start(TutorialLesson lesson)
    {
        _lesson=lesson;_finished=false;_next.Visible=false;
        _title.Text=lesson.Chapter+"  /  "+lesson.Title;_objective.Text="Get ready...";
        foreach(var button in _actions.GetChildren().OfType<Button>())button.FocusMode=FocusModeEnum.None;
        Refresh();
    }
    public void State(JsonElement state)
    {
        if(_lesson is null || !state.TryGetProperty("tutorial",out var data) || data.ValueKind!=JsonValueKind.Object)return;
        bool complete=data.GetProperty("complete").GetBoolean(),failed=data.GetProperty("failed").GetBoolean();
        int step=data.GetProperty("step").GetInt32();
        _objective.Text=complete?"Lesson clear!":failed?"Give it another try":state.GetProperty("phase").GetInt32()==0?"Get ready...":$"{step+1} / {_lesson.Objectives.Length}   {_lesson.Objectives[Math.Min(step,_lesson.Objectives.Length-1)]}";
        if(complete||failed)
        {
            _instructions.Text=data.GetProperty("feedback").GetString();
            _next.Visible=complete;_next.Text=_lesson==TutorialCourse.Lessons[^1]?"Back to lessons":"Next lesson";
            foreach(var button in _actions.GetChildren().OfType<Button>())button.FocusMode=FocusModeEnum.All;
            if(!_finished){_finished=true;(complete?_next:_retry).CallDeferred(Control.MethodName.GrabFocus);}
        }
    }
    public void FocusAction(){if(_finished)(_next.Visible?_next:_retry).GrabFocus();}
    private void Refresh()
    {
        if(_lesson is null)return;
        if(!_finished)_instructions.Text=Instructions(_lesson.Description);
        _tip.Text=_lesson.Tip+"  ·  "+(ControllerHub.Current.ActiveFamily==ControllerFamily.Keyboard?"Esc":ControllerProfile.ButtonName(ControllerHub.Current.ActiveFamily,JoyButton.Start))+": pause";
    }
    public static string Instructions(string text)
    {
        var f=ControllerHub.Current.ActiveFamily;
        bool keys=f==ControllerFamily.Keyboard;
        return text.Replace("{move}",keys?"WASD":"the stick / D-pad")
            .Replace("{bomb}",keys?"J":ControllerProfile.BombName(f))
            .Replace("{carry}",keys?"K":ControllerProfile.ButtonName(f,JoyButton.LeftShoulder))
            .Replace("{aim}",keys?"L":ControllerProfile.AimName(f))
            .Replace("{remote}",keys?"I":ControllerProfile.ButtonName(f,ControllerProfile.RemoteButton(f)))
            .Replace("{stop}",keys?"O":ControllerProfile.ButtonName(f,JoyButton.RightShoulder));
    }
}
