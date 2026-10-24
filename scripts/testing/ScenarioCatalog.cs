using Godot;
using System.Reflection;
using System.Text.Json;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private string _activeScenario="startup",_catalogHash="";
    private readonly List<string> _completedScenarios=[];
    private async Task RunRegisteredScenario(string name)
    {
        using var file=Godot.FileAccess.Open("res://assets/testing/scenarios.json",Godot.FileAccess.ModeFlags.Read)
            ??throw new IOException("Missing bundled playtest catalog");
        using var document=JsonDocument.Parse(file.GetAsText());
        if(document.RootElement.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Unsupported scenario catalog");
        var scenarios=document.RootElement.GetProperty("scenarios").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!);
        async Task Run(string id,HashSet<string> stack)
        {
            if(!stack.Add(id))throw new InvalidDataException("Scenario suite cycle: "+id);
            if(!scenarios.TryGetValue(id,out var entry))throw new ArgumentException("Unknown scenario: "+id);
            if(_headless&&!entry.GetProperty("headless").GetBoolean())throw new ArgumentException("Scenario requires graphical rendering: "+id);
            if(entry.TryGetProperty("steps",out var steps))
                foreach(var step in steps.EnumerateArray())await Run(step.GetString()!,stack);
            else
            {
                if(!entry.TryGetProperty("method",out var method))throw new ArgumentException("Script scenario requires --playtest-file");
                _activeScenario=id;ProgressCheckpoint();Event("scenario-start",new {id,frame=Engine.GetProcessFrames()});
                var implementation=typeof(PlaytestDriver).GetMethod(method.GetString()!,BindingFlags.NonPublic|BindingFlags.Instance)
                    ??throw new MissingMethodException("Unimplemented scenario: "+id);
                int before=_checks.Count;
                if(implementation.Invoke(this,null) is not Task task)throw new InvalidDataException("Scenario must return Task: "+id);
                await task;
                if(_checks.Count==before)throw new InvalidOperationException("Scenario produced no checks: "+id);
                _completedScenarios.Add(id);Event("scenario-complete",new {id,frame=Engine.GetProcessFrames()});
            }
            stack.Remove(id);
        }
        await Run(name,[]);
    }
    private void ProgressCheckpoint()=>File.WriteAllText(Path.Combine(_output,"progress.json"),JsonSerializer.Serialize(new
        {schemaVersion=1,scenario=_scenario,activeScenario=_activeScenario,frame=_frame,assertions=_checks.Count,screen=_root.ScreenName,seconds=_clock.Elapsed.TotalSeconds}));
}
