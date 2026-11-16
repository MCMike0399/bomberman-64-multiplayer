using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace BlastCircuit.Testing.Fixtures;

/// <summary>Collects evidence after a required predicate fails, without ever clearing that failure.</summary>
internal static class RetainedFailureObservation
{
    internal static async Task RunAsync(Func<Task> required,Func<object> snapshot,
        Func<JsonElement,CancellationToken,Task> observe,Action<Exception> observationFailed,
        TimeSpan? observationBudget=null)
    {
        try { await required(); }
        catch(Exception failure)
        {
            var original=ExceptionDispatchInfo.Capture(failure);
            void Report(Exception error){try{observationFailed(error);}catch(Exception){}}
            using var cancellation=new CancellationTokenSource();
            try
            {
                var budget=observationBudget??TimeSpan.FromSeconds(10);
                if(budget<=TimeSpan.Zero||budget>TimeSpan.FromSeconds(10))
                    throw new ArgumentOutOfRangeException(nameof(observationBudget));
                // Serialize before the first observation await: mutable native
                // counters/lists must not rewrite the original failing state.
                var first=JsonSerializer.SerializeToElement(snapshot());
                await observe(first,cancellation.Token).WaitAsync(budget);
            }
            catch(Exception error)
            {
                // Even an evidence-write error must not replace the required
                // predicate's exception. Its type can be retained separately.
                Report(error);
            }
            finally { try{cancellation.Cancel();}catch(Exception error){Report(error);} }
            original.Throw();
        }
    }
}
