using Godot;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task SeaSickShapeStudy()
    {
        Event("sea-sick-shape-policy",new{concept="The Tubby Cutter",authorityMutation=false,axis="left stern / right bow",inspection="explicit grayscale material study; art approval remains visual"});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var original=_root.Preferences.Graphics;
        if(!_headless)await ResizeWindow(new(1280,800));
        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,SuddenDeath:false)
            {Advanced=new AdvancedRules{BlockPercent=0}},4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Tubby Cutter ordinary local battle starts",150);
        var arena=LightingArena;var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
        var environment=AuditDescendants(arena).OfType<WorldEnvironment>().Single().Environment;
        float saturation=environment.AdjustmentSaturation;var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;
        try
        {
            var problems=AuditModelProblems(terrain,out int meshes,out int vertices);
            Check(problems.Count==0,$"loft, caps and deck faces have finite geometry and matching clockwise winding: {string.Join("; ",problems.Take(4))}");
            var hull=terrain.GetNode<MeshInstance3D>("CutterHull");var bounds=hull.Mesh.GetAabb();
            Check(bounds.Size.X>25&&bounds.Size.Z>21&&bounds.Position.X<0&&bounds.End.X>terrain.Width,
                "loft makes a broad left-right ship body beyond the unchanged playable rectangle");
            Event("tubby-cutter-shape",new{meshes,vertices,bounds=bounds.ToString(),layout=terrain.Layout.Id});
            var hullVertices=hull.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Event("tubby-near-profile",new{samples=hullVertices.Where(p=>Math.Abs(p.X-9.5f)<.001f&&p.Z>19&&p.Y> -2.2f)
                .Select(p=>new{local=p.ToString(),screen=camera.UnprojectPosition(hull.ToGlobal(p)).ToString()}).ToArray()});
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.High})
            {
                await SelectGraphics(preset);terrain.SetSceneryVisible(false);terrain.SetShipShapeStudy(true);
                environment.AdjustmentSaturation=0;
                CheckFraming(BlastStages.SeaSick,GetWindow().Size);
                await Capture("sea-sick-shape-"+GraphicsQuality.ToWire(preset)+"-battle");
                for(int frame=0;frame<420&&Math.Abs(arena.ShipRollSine)<.01045f;frame++)await Frames(1);
                Check(Math.Abs(arena.ShipRollSine)>=.01045f,"study observes an actual maximum-roll phase without posing the ship");
                await Capture("sea-sick-shape-"+GraphicsQuality.ToWire(preset)+"-maximum-roll");
            }
            if(!_headless)
            {
                camera.Position=new(38,29,41);camera.LookAt(new Vector3(9.5f,-.5f,9.5f));
                await Capture("sea-sick-shape-oblique-inspection");camera.Transform=pose;
                camera.Position=new(9.5f,12,42);camera.LookAt(new Vector3(9.5f,-.3f,9.5f));
                await Capture("sea-sick-shape-near-profile-inspection");camera.Transform=pose;
            }
        }
        finally
        {
            camera.Transform=pose;environment.AdjustmentSaturation=saturation;terrain.SetShipShapeStudy(false);
            _root.Back();await Frames(3);await SelectGraphics(original);
        }
    }
}
