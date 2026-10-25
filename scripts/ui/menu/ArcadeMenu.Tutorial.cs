using Godot;
using Bomber.Game;
using BlastCircuit.UI.Hud;
namespace BlastCircuit.UI.Menu;

/// <summary>Replayable how-to lessons, grouped by mechanic, with device-aware instructions.</summary>
public partial class ArcadeMenu
{
    private void BuildTutorial()
    {
        var screen=Screen("Tutorial");
        var title=ArcadeTheme.Heading(screen,"How to play",48);title.Position=new(80,54);
        _lessonProgress=ArcadeTheme.Text(screen,"Circuit Garden · your training ground",18,ArcadeTheme.Muted);
        _lessonProgress.Position=new(84,122);
        var back=ArcadeTheme.Quiet(ArcadeTheme.Button(screen,"Back",()=>BackRequested?.Invoke()));
        back.Name="TutorialBack";back.Position=new(1195,73);back.Size=new(140,48);
        var list=Card(screen,new(64,166),new(538,620));
        var scroll=new ScrollContainer {Name="TutorialLessons",CustomMinimumSize=new(490,548),SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};list.AddChild(scroll);
        var choices=ArcadeTheme.Column(scroll,7);choices.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        string chapter="";
        foreach(var lesson in TutorialCourse.Lessons)
        {
            if(chapter!=lesson.Chapter)
            {chapter=lesson.Chapter;var label=ArcadeTheme.Text(choices,chapter,17,ArcadeTheme.Gold);label.CustomMinimumSize=new(0,32);}
            var button=ArcadeTheme.Button(choices,lesson.Title,()=>TutorialRequested?.Invoke(lesson.Id));
            button.Name="Lesson_"+lesson.Id;button.CustomMinimumSize=new(460,49);button.AddThemeFontSizeOverride("font_size",17);
            button.FocusEntered+=()=>SelectLesson(lesson);_lessonButtons[lesson.Id]=button;
        }
        var detail=Card(screen,new(630,166),new(706,620));
        ArcadeTheme.Text(detail,"CIRCUIT GARDEN",16,ArcadeTheme.Gold);
        _lessonTitle=ArcadeTheme.Heading(detail,"",32);
        _lessonCopy=Copy(detail,"",21);_lessonCopy.CustomMinimumSize=new(630,120);
        _lessonObjectives=Copy(detail,"",19);_lessonObjectives.CustomMinimumSize=new(0,92);
        _lessonTip=Hint(detail,"",18);_lessonTip.SizeFlagsVertical=SizeFlags.ExpandFill;
        _lessonStart=ArcadeTheme.Button(detail,"Try it!",()=>TutorialRequested?.Invoke(_selectedLesson.Id));
        _lessonStart.Name="StartLesson";_lessonStart.CustomMinimumSize=new(0,64);
        var practice=ArcadeTheme.Quiet(ArcadeTheme.Button(detail,"Free practice in Circuit Garden",()=>TutorialRequested?.Invoke("free")));
        practice.Name="TutorialFreePractice";practice.CustomMinimumSize=new(0,48);practice.AddThemeFontSizeOverride("font_size",17);
        RegisterNavigation("Tutorial",[back],[],_lessonButtons.Values.Cast<Control>().ToArray(),new Control[]{_lessonStart,practice});
        SelectLesson(_selectedLesson);
    }

    private void SelectLesson(TutorialLesson lesson)
    {
        _selectedLesson=lesson;_lessonTitle.Text=lesson.Title;
        _lessonCopy.Text=TutorialHud.Instructions(lesson.Description);
        _lessonObjectives.Text=string.Join('\n',lesson.Objectives.Select((text,index)=>$"{index+1}. {text}"));
        _lessonTip.Text=lesson.Tip;
        _lessonStart.Text=_completedLessons.Contains(lesson.Id)?"Play again":"Try it!";
    }
    public void RememberLesson(string id)
    {
        SelectLesson(TutorialCourse.Get(id));
        _savedFocus["Tutorial"]=_lessonButtons[id];
        _columnFocus[("Tutorial",0)]=_lessonButtons[id];
    }
    public void TutorialProgress(IEnumerable<string> completed)
    {
        _completedLessons.Clear();_completedLessons.UnionWith(completed.Where(TutorialCourse.Contains));
        foreach(var lesson in TutorialCourse.Lessons)_lessonButtons[lesson.Id].Text=(_completedLessons.Contains(lesson.Id)?"★  ":"")+lesson.Title;
        _lessonProgress.Text=$"Circuit Garden · {_completedLessons.Count} / {TutorialCourse.Lessons.Count} lessons cleared";
        SelectLesson(_selectedLesson);
    }
    private void RefreshTutorialPrompts(){if(_lessonCopy!=null)SelectLesson(_selectedLesson);}
}
