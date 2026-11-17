using System.Collections.Concurrent;
using System.Text.Json;
using Godot;

namespace BlastCircuit.Testing.Fixtures;

/// <summary>
/// QA diagnostic: records which thread raised each engine error or warning. It
/// is an additional observer only; Godot still prints every message and the
/// playtest runner still treats runtime errors as failures.
/// </summary>
public partial class ErrorThreadRecorder : Godot.Logger
{
    /// <summary>Main-thread fixture step active when an error is raised elsewhere.</summary>
    public static volatile string Phase="unset";
    private readonly ulong _mainThread=OS.GetMainThreadId();
    private readonly ConcurrentQueue<object> _rows=new();

    public override void _LogError(string function,string file,int line,string code,string rationale,bool editorNotify,int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var thread=System.Threading.Thread.CurrentThread;ulong caller=OS.GetThreadCallerId();
        _rows.Enqueue(new{function,file,line,code,rationale,errorType,phase=Phase,
            godotThreadId=caller.ToString(),isGodotMainThread=caller==_mainThread,
            workerPoolTaskId=WorkerThreadPool.GetCallerTaskId(),workerPoolGroupId=WorkerThreadPool.GetCallerGroupId(),
            managedThreadId=thread.ManagedThreadId,managedThreadName=thread.Name,thread.IsThreadPoolThread,thread.IsBackground,
            managedStack=System.Environment.StackTrace});
    }

    public int Count=>_rows.Count;

    public void Write(string path)=>File.WriteAllText(path,JsonSerializer.Serialize(new{schemaVersion=1,
        scope="Diagnostic thread attribution for engine errors; never a pass/fail signal or suppression",
        mainThreadId=_mainThread.ToString(),errors=_rows.ToArray()},new JsonSerializerOptions{WriteIndented=true,NewLine="\n"}));
}
