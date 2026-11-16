namespace BlastCircuit.Rendering;

/// <summary>Sequential preparation fence policy. One missed normal draw selects
/// real forced rendering for the rest of this owner's batch, never the next one.</summary>
public sealed class PreparationDrawBatch:IDisposable
{
    // Overlay1 + two effect poses with3 draws each; every miniature has one
    // loading draw and3 content draws. The probe independently pins this contract.
    public const int FixedPreparationDraws=7,DrawsPerMiniature=4;
    public static int ExpectedDraws(int miniatures)=>miniatures>=0
        ?checked(FixedPreparationDraws+DrawsPerMiniature*miniatures):throw new ArgumentOutOfRangeException(nameof(miniatures));
    private bool _disposed;
    public bool ForceRendering {get;private set;}
    public int NormalWaits {get;private set;}
    public int ForcedDraws {get;private set;}
    public bool Completed=>_disposed;

    public async Task CompleteAsync(Task normalDraw,Func<Task> deadline,Func<Task> forceRender,CancellationToken stopping)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        stopping.ThrowIfCancellationRequested();
        if(!ForceRendering)
        {
            NormalWaits++;
            if(await Task.WhenAny(normalDraw,deadline())==normalDraw)
            {
                stopping.ThrowIfCancellationRequested();
                await normalDraw;
                return;
            }
            stopping.ThrowIfCancellationRequested();
            ForceRendering=true;
        }
        stopping.ThrowIfCancellationRequested();
        await forceRender();
        ForcedDraws++;
        stopping.ThrowIfCancellationRequested();
    }

    public void Dispose(){ForceRendering=false;_disposed=true;}
}
