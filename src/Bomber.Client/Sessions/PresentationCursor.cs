using System.Text.Json;
namespace Bomber.Client.Sessions;

/// <summary>Consumes accepted effects once; first state establishes a silent join/reconnect baseline.</summary>
public sealed class PresentationCursor
{
    private bool _initialized;
    public long Sequence {get;private set;}
    public void Reset(){_initialized=false;Sequence=0;}
    public IReadOnlyList<JsonElement> Read(JsonElement state)
    {
        if(!state.TryGetProperty("presentationSeq",out var latest)||!state.TryGetProperty("events",out var events))return [];
        long sequence=latest.GetInt64();
        if(!_initialized){_initialized=true;Sequence=sequence;return [];}
        if(sequence<=Sequence)return [];
        var fresh=events.EnumerateArray().Where(e=>e.GetProperty("seq").GetInt64()>Sequence&&e.GetProperty("seq").GetInt64()<=sequence)
            .OrderBy(e=>e.GetProperty("seq").GetInt64()).Select(e=>e.Clone()).ToArray();
        Sequence=sequence;return fresh;
    }
}
