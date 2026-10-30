using Godot;
using BlastCircuit.App;
using System.Runtime.InteropServices;
using System.Text.Json;

public partial class ProbeResources:RefCounted
{
    private static readonly List<Resource> Kept=new();
    public void Prepare(bool fenced)
    {
        for(int index=0;index<1000;index++)Kept.Add(index%2==0?new CylinderMesh():new StandardMaterial3D());
        MakeTemporary();GC.Collect();
        GD.Print("BLAST_RESOURCE_PENDING "+GC.GetGCMemoryInfo().FinalizationPendingCount+" framework="+RuntimeInformation.FrameworkDescription);
        if(fenced)
        {
            var result=ManagedFinalizerDrain.Run(TimeSpan.FromSeconds(5));
            GD.Print("BLAST_FINALIZERS_RESULT "+JsonSerializer.Serialize(result));
            if(!result.Completed)throw new InvalidOperationException(result.Detail);
        }
    }
    private static void MakeTemporary()
    {
        for(int index=0;index<20000;index++){var resource=new StandardMaterial3D();GC.KeepAlive(resource);}
    }
}
