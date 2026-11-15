using Godot;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildSeaSickExterior()
    {
        var scenery=_scenery!;scenery.TopLevel=true;
        // Open water is intentional negative space around the cutter's own
        // silhouette. Only two distant gulls remain; no cloned harbor kit.
        Gull(scenery,new(-5,2.5f,-3),2.0f,.14f,0);
        Gull(scenery,new(Width+6,2.8f,Depth-1),2.0f,-.12f,2);
    }
}
