using Godot;
using BlastCircuit.Arena;
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
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        if(!_headless)await ResizeWindow(new(1280,800));
        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,SuddenDeath:false)
            {Advanced=new AdvancedRules{BlockPercent=0}},4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Tubby Cutter ordinary local battle starts",150);
        await SelectLocalLighting(StageTime.Midday);
        var arena=LightingArena;var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
        var environment=AuditDescendants(arena).OfType<WorldEnvironment>().Single().Environment;
        float saturation=environment.AdjustmentSaturation;var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;
        try
        {
            var problems=AuditModelProblems(terrain,out int meshes,out int vertices);
            Check(problems.Count==0,$"loft, caps and deck faces have finite geometry and matching clockwise winding: {string.Join("; ",problems.Take(4))}");
            var hull=terrain.GetNode<MeshInstance3D>("CutterHull");var bounds=hull.Mesh.GetAabb();
            Check(bounds.Size.X>25&&bounds.Size.Z>18.5f&&bounds.Position.X<0&&bounds.End.X>terrain.Width,
                "loft makes a broad left-right ship body beyond the unchanged playable rectangle");
            Event("tubby-cutter-shape",new{meshes,vertices,bounds=bounds.ToString(),layout=terrain.Layout.Id});
            var hullVertices=hull.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Event("tubby-near-profile",new{samples=hullVertices.Where(p=>Math.Abs(p.X-9.5f)<.001f&&p.Z>18.3f&&p.Y> -2.2f)
                .Select(p=>new{local=p.ToString(),screen=camera.UnprojectPosition(hull.ToGlobal(p)).ToString()}).ToArray()});
            async Task<Vector2> CaptureRoll(string label)
            {
                CheckFraming(BlastStages.SeaSick,GetWindow().Size);
                await Capture(label,0); // The returned telemetry is from the captured post-draw frame.
                var near=arena.ScreenPoint(new System.Numerics.Vector3(9.5f,0,17.5f));
                var far=arena.ScreenPoint(new System.Numerics.Vector3(9.5f,0,1.5f));
                Event("ship-roll-capture",new{label,clock=arena.ShipMotionClock,rollSine=arena.ShipRollSine,
                    rollDegrees=Mathf.Asin(arena.ShipRollSine)*180/Mathf.Pi,near=near.ToString(),far=far.ToString(),
                    parent=arena.StageAssemblyTransform.ToString(),terrain=terrain.GlobalTransform.ToString(),
                    ocean=terrain.GetNode<MeshInstance3D>("ShipOcean").GlobalTransform.ToString(),
                    camera=camera.GetCameraTransform().ToString(),hOffset=camera.HOffset,vOffset=camera.VOffset});
                return near;
            }
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.High})
            {
                await SelectGraphics(preset);terrain.SetSceneryVisible(false);terrain.SetShipShapeStudy(true);
                environment.AdjustmentSaturation=0;
                CheckFraming(BlastStages.SeaSick,GetWindow().Size);
                await Capture("sea-sick-shape-"+GraphicsQuality.ToWire(preset)+"-battle");
                for(int frame=0;frame<420&&Math.Abs(arena.ShipRollSine)<.0347f;frame++)await Frames(1);
                Check(Math.Abs(arena.ShipRollSine)>=.0347f,"study observes an actual maximum-roll phase without posing the ship");
                var firstLandmark=await CaptureRoll("sea-sick-shape-"+GraphicsQuality.ToWire(preset)+"-maximum-roll");
                if(preset==GraphicsPreset.High)
                {
                    float firstSign=Mathf.Sign(arena.ShipRollSine);
                    for(int frame=0;frame<420&&(Mathf.Sign(arena.ShipRollSine)==firstSign||Math.Abs(arena.ShipRollSine)<.0347f);frame++)await Frames(1);
                    Check(Mathf.Sign(arena.ShipRollSine)!=firstSign&&Math.Abs(arena.ShipRollSine)>=.0347f,"study observes the opposite real roll against fixed water");
                    var oppositeLandmark=await CaptureRoll("sea-sick-shape-high-opposite-roll");
                    Check(firstLandmark.DistanceTo(oppositeLandmark)>3,"opposite actual roll moves the rendered deck landmark visibly against fixed water");
                }
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
            _root.Back();await Frames(3);await SelectGraphics(original);await SelectLocalLighting(lighting);
        }
    }
}
