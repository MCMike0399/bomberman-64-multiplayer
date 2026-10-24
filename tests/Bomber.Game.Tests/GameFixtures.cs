using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

/// <summary>Shared deterministic match helpers for the integration checks run from Program.cs.</summary>
internal static class GameFixtures
{
    internal static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    internal static JsonElement Data(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.GetProperty("d").Clone(); }
    internal static JsonElement State(LocalMatch match) => Data(match.StateJson!);
    internal static LocalMatch TwoHumans() => new(new MatchOptions { HumanPlayers = 2, BotPlayers = 0 });
    internal static void Step(LocalMatch match, int ticks) { for (var i = 0; i < ticks; i++) match.Tick(); }
    internal static void Fight(LocalMatch match) { Step(match, 62); Assert(State(match).GetProperty("phase").GetInt32() == 1, "Fight did not begin."); }

    internal static StageLayout StepArena((int X, int Z) spawn)
    {
        var cells = new CellDef[9, 9];
        for (var x = 0; x < 9; x++) for (var z = 0; z < 9; z++) cells[x, z] = x >= 4 ? CellDef.Floor1 : CellDef.Floor0;
        cells[3, 3] = CellDef.Ramp(0, 1, 1, 0);
        return new StageLayout { Id = "fixture", Width = 9, Height = 9, Cells = cells, Spawns = [spawn] };
    }

    internal static JsonElement Player(LocalMatch match, int index=0)=>State(match).GetProperty("players")[index];
    internal static void Walk(LocalMatch match,float x,float z)
    {
        for(var tick=0;tick<600;tick++)
        {
            var p=Player(match);var dx=x-p.GetProperty("x").GetSingle();var dz=z-p.GetProperty("z").GetSingle();
            if(Math.Abs(dx)<.12f&&Math.Abs(dz)<.12f){match.SetInput(0,default);Step(match,2);return;}
            match.SetInput(0,new PlayerInput(Math.Clamp(dx/.3f,-1,1),Math.Clamp(dz/.3f,-1,1)));match.Tick();
        }
        var last=Player(match);throw new InvalidOperationException($"Could not walk to {x},{z}; stuck at {last.GetProperty("x")},{last.GetProperty("y")},{last.GetProperty("z")}");
    }
    internal static StageLayout FlatArena((int X,int Z)[] spawns,int size=9)
    {
        var cells=new CellDef[size,size];for(var x=0;x<size;x++)for(var z=0;z<size;z++)cells[x,z]=CellDef.Floor0;
        return new StageLayout {Id="fixture",Width=size,Height=size,Cells=cells,Spawns=spawns,SoftBlockDensity=0};
    }

    internal static JsonElement[] Events(LocalMatch match)=>State(match).GetProperty("events").EnumerateArray().ToArray();
    internal static string? Kind(JsonElement e)=>e.GetProperty("kind").GetString();
}
