using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task CopperLockWaterInputs()
    {
        try
        {
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),4,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"water-input audit enters an ordinary Copper Lock match",180);
            CheckCopperWaterInputs(AuditDescendants(LightingArena).OfType<TempleStageView>().Single());
        }
        finally{_root.Navigate("Modes");await Frames(3);}
    }

    private void CheckCopperWaterInputs(TempleStageView terrain)
    {
        foreach(var (name,left,right,back,front,height) in new[]{
            ("GutterShallowWater",1f,terrain.Width-1f,1f,terrain.Depth-1f,.035f),
            ("CopperLockOuterWater",-55f,terrain.Width+55f,-55f,terrain.Depth+55f,-.96f)})
        {
            var node=terrain.GetNode<MeshInstance3D>(name);
            var toArena=terrain.GlobalTransform.AffineInverse()*node.GlobalTransform;
            var origin=toArena.Origin;
            var radii=new[]{new Vector2(1,1),new Vector2(terrain.Width-1,1),
                new Vector2(1,terrain.Depth-1),new Vector2(terrain.Width-1,terrain.Depth-1)}
                .Select(p=>p.DistanceTo(new(origin.X,origin.Z))).ToArray();
            Event("copper-water-radial-coverage",new{name,origin=origin.ToString(),minimum=radii.Min(),maximum=radii.Max()});
            Check(radii.Max()-radii.Min()<.0001f&&radii.Max()<26,
                "all opposite court corners receive symmetric in-range reflection coverage: "+name);
            int inspected=0;float error=0;
            for(int surface=0;surface<node.Mesh.GetSurfaceCount();surface++)
            {
                var arrays=node.Mesh.SurfaceGetArrays(surface);
                var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                Check(vertices.Length==uv.Length,"water has one original UV per consumed vertex: "+name);
                for(int i=0;i<vertices.Length;i++)
                {
                    var expected=new Vector3(Mathf.Lerp(left,right,uv[i].X),height,Mathf.Lerp(back,front,uv[i].Y));
                    error=Math.Max(error,(toArena*vertices[i]).DistanceTo(expected));inspected++;
                }
            }
            Event("copper-water-geometry-equivalence",new{name,inspected,maximumWorldError=error});
            Check(inspected>0&&error<.00002f,"centering preserves the actual legacy water world positions and UV mapping: "+name);
        }
    }
}
