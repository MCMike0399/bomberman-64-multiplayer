using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Tutorial;
using BlastCircuit.InputDevices;
using BlastCircuit.UI.Hud;
namespace BlastCircuit.App;

public partial class GameRoot
{
    private void BuildTutorialHud(CanvasLayer layer)
    {
        _tutorialHud=new TutorialHud {Visible=false};layer.AddChild(_tutorialHud);
        _menu.TutorialRequested+=StartTutorial;
        _tutorialHud.RetryRequested+=()=>{if(_tutorialLessonId is {} id)StartTutorial(id);};
        _tutorialHud.NextRequested+=NextTutorial;
        _tutorialHud.LessonsRequested+=()=>Navigate("Tutorial");
    }
    public void StartTutorial(string id)
    {
        if(_connecting)return;
        bool free=id=="free";
        if(!free)_menu.RememberLesson(id);
        var settings=new SessionSettings(Stage:BlastStages.CircuitGarden,Bots:0,SuddenDeath:false,BotDifficulty:"easy");
        var session=new LocalSession(settings,seed:free?System.Random.Shared.Next():64,practice:true,tutorialLesson:free?null:id);
        _lastSettings=settings;_lastHumans=1;_lastAppearances=null;_practice=true;_lastServer=null;
        Begin(session,false);
        _tutorialLessonId=free?null:id;_trainingSession=true;
        if(!free)_tutorialHud.Start(TutorialCourse.Get(id));
        ShowTutorialHud();
        ControllerHub.Current.RearmControls();
    }
    private void NextTutorial()
    {
        int index=TutorialCourse.Lessons.ToList().FindIndex(lesson=>lesson.Id==_tutorialLessonId);
        if(index>=0&&index<TutorialCourse.Lessons.Count-1)StartTutorial(TutorialCourse.Lessons[index+1].Id);
        else Navigate("Tutorial");
    }
    private void ShowTutorialHud()
    {
        if(_tutorialHud==null)return;
        _tutorialHud.Visible=_tutorialLessonId is not null && ScreenName=="Match";
        if(_tutorialHud.Visible)
        {
            _hud.Visible=false;
            ControllerHub.Current.MenuNavigationEnabled=_tutorialHud.Finished;
            if(_tutorialHud.Finished)_tutorialHud.FocusAction();
        }
    }
    private void TutorialState(JsonElement state)
    {
        if(_tutorialLessonId is null)return;
        _arena.TutorialTarget(state);
        _tutorialHud.State(state);
        if(_tutorialHud.Finished)
        {
            _session?.ReleaseInputs();_injected.Clear();
            if(ScreenName=="Match")ControllerHub.Current.MenuNavigationEnabled=true;
        }
        if(state.TryGetProperty("tutorial",out var tutorial)&&tutorial.ValueKind==JsonValueKind.Object&&tutorial.GetProperty("complete").GetBoolean()
            &&_preferences.CompletedTutorials.Add(_tutorialLessonId))
        {
            if(_preferences.Save(PreferencesPath)!=Error.Ok)GD.PushWarning("Could not save tutorial progress on this device.");
            _menu.TutorialProgress(_preferences.CompletedTutorials);
        }
    }
}
