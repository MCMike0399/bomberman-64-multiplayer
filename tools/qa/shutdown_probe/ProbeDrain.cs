using Godot;
using BlastCircuit.App;
using System.Text.Json;
public partial class ProbeDrain : RefCounted
{
    public bool Drain()
    {
        if(!ShutdownDrain.MatchesEnginePolicy(4,7,2,"ed1daf0bf001b61586d9930840f2f1394092c079")||
            ShutdownDrain.MatchesEnginePolicy(4,8,0,"ed1daf0bf")||ShutdownDrain.MatchesEnginePolicy(4,7,3,"ed1daf0bf")||
            ShutdownDrain.MatchesEnginePolicy(4,7,2,"different-commit"))throw new InvalidOperationException("Engine policy guard failed");
        GD.Print("BLAST_SHUTDOWN_ENGINE_POLICY_GUARD_OK");
        var result=ShutdownDrain.Run(TimeSpan.FromSeconds(5));
        GD.Print("BLAST_DRAIN_RESULT "+JsonSerializer.Serialize(result));
        return result.Completed;
    }
}
