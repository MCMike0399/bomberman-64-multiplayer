using BlastCircuit.App;
using BlastCircuit.Testing.Fixtures;

internal static class RetainedFailureObservationChecks
{
    internal static async Task<int> Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        bool sampled=false,observed=false;
        await RetainedFailureObservation.RunAsync(()=>Task.CompletedTask,
            ()=>{sampled=true;return new{connected=true};},
            (_,_)=>{observed=true;return Task.CompletedTask;},_=>throw new Exception("unexpected observation failure"));
        Check(!sampled&&!observed,"passing connection never freezes or runs post-failure observation");

        var original=FailureAtRequiredDeadline();bool connected=false,frozen=false;int samples=0;Exception? unexpected=null;
        var live=new Dictionary<string,object>{{"connected",false},{"signals",3}};
        Exception? caught=null;
        try
        {
            await RetainedFailureObservation.RunAsync(()=>Task.FromException(original),()=>live,async(first,_)=>
            {
                await Task.Yield();connected=true;live["connected"]=true;live["signals"]=4;samples++;
                frozen=!first.GetProperty("connected").GetBoolean()&&first.GetProperty("signals").GetInt32()==3;
            },error=>unexpected=error);
        }
        catch(Exception error){caught=error;}
        Check(frozen&&unexpected is null,"first deadline failure stays immutable after the live pair connects and counters change");
        Check(connected&&samples==1&&ReferenceEquals(caught,original),"late native connection still throws the exact original required failure");
        Check(caught!.StackTrace!.Contains(nameof(FailureAtRequiredDeadline),StringComparison.Ordinal),"original required-failure stack is preserved");

        var observationError=new IOException("evidence write failed");Exception? reported=null;caught=null;
        try
        {
            await RetainedFailureObservation.RunAsync(()=>Task.FromException(original),()=>new{connected=false},
                (_,_)=>Task.FromException(observationError),error=>reported=error);
        }
        catch(Exception error){caught=error;}
        Check(ReferenceEquals(caught,original)&&ReferenceEquals(reported,observationError),"evidence error is reported without replacing the required failure");
        caught=null;
        try
        {
            await RetainedFailureObservation.RunAsync(()=>Task.FromException(original),()=>new{connected=false},
                (_,_)=>Task.FromException(observationError),_=>throw new IOException("reporter also failed"));
        }
        catch(Exception error){caught=error;}
        Check(ReferenceEquals(caught,original),"even a failing evidence reporter cannot mask the original failure");

        bool cancelled=false;reported=null;caught=null;
        var stalled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wall=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await RetainedFailureObservation.RunAsync(()=>Task.FromException(original),()=>new{connected=false},
                (_,token)=>{token.Register(()=>cancelled=true);return stalled.Task;},error=>reported=error,TimeSpan.FromMilliseconds(20));
        }
        catch(Exception error){caught=error;}
        Check(ReferenceEquals(caught,original)&&reported is TimeoutException&&cancelled&&wall.Elapsed<TimeSpan.FromSeconds(2),
            "non-completing observation is bounded, cancelled and still retains the required failure");

        foreach(var(address,family,scope)in new[]{("127.0.0.1","IPv4","loopback"),("10.2.3.4","IPv4","private"),
            ("172.16.4.5","IPv4","private"),("192.168.2.3","IPv4","private"),("169.254.2.3","IPv4","link-local"),
            ("100.64.2.3","IPv4","shared"),("203.0.113.7","IPv4","global-or-other"),("::1","IPv6","loopback"),
            ("fe80::1","IPv6","link-local"),("fd00::1","IPv6","unique-local"),("::ffff:10.2.3.4","IPv6-mapped-IPv4","private"),
            ("host.local","hostname","unresolved"),("","unknown","unknown")})
            Check(GodotPeerLink.CandidateAddressSummary(address)==(family,scope),"candidate summary retains only family/scope: "+family+"/"+scope);
        var safe=GodotPeerLink.SafeDiagnosticSnapshot(new{candidates=new[]{new{
            address="10.2.3.4",port=54321,addressFamily="IPv4",addressScope="private",sha256="candidate-hash"}},
            sdpEvents=new[]{new{type="offer",sha256="sdp-hash"}}});
        var candidate=safe.GetProperty("candidates")[0];
        Check(!candidate.TryGetProperty("address",out _)&&!candidate.TryGetProperty("port",out _)&&
            !safe.GetRawText().Contains("10.2.3.4",StringComparison.Ordinal)&&
            candidate.GetProperty("addressFamily").GetString()=="IPv4"&&candidate.GetProperty("addressScope").GetString()=="private",
            "actual safe snapshot keeps categories and hashes while removing raw candidate addresses and ports");
        Console.WriteLine($"PASS {checks} retained-failure evidence checks");return checks;
    }

    private static Exception FailureAtRequiredDeadline()
    {
        try{throw new InvalidOperationException("the original five-second connection predicate failed");}
        catch(Exception error){return error;}
    }
}
