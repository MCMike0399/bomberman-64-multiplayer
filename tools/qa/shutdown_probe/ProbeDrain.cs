using Godot;
using BlastCircuit.App;
using System.Text.Json;
public partial class ProbeDrain : RefCounted
{
    public bool Drain()
    {
        var result=ShutdownDrain.Run(TimeSpan.FromSeconds(5));
        GD.Print("BLAST_DRAIN_RESULT "+JsonSerializer.Serialize(result));
        return result.Completed;
    }
}
