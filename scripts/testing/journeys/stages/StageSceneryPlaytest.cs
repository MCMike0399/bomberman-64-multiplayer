using BlastCircuit.Rendering;
using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private static readonly (string Stage,string[] Signature)[] SceneryStages=
    [
        (BlastStages.SunkenTemple,["Obelisk","SunkenColonnade","SunkenGate","Sailboat","PalmTree","Gull"]),
        (BlastStages.RockGarden,["Windmill","Footbridge","StoneLantern","FlowerBed","Butterfly","GardenRuin"]),
        (BlastStages.UpAndDown,["Lighthouse","SeaStack","Buoy","Jetty","Sailboat","Gull"]),
        (BlastStages.TopRules,["FortressCrag","Turret","WallBanner","Cloud","FloatingIslet","Starfield"]),
        (BlastStages.BlizzardBattle,["Igloo","Snowman","Penguin","PineTree","FrozenPond","GlacierPeak"]),
        (BlastStages.GreedyTrap,["Rowboat","Sailboat","Gull"]),
        (BlastStages.CircuitGarden,["TargetRaft","Jetty","Buoy","PalmTree"]),
    ];

    /// <summary>Decorative scenery around every arena: placement outside the playable
    /// volume, no collision, real animation, flood riding and menu trimming.</summary>
    private async Task StageSceneryTour()
    {
        Event("stage-scenery-policy",new{ordinaryInputs=true,stateMutation=false,inspectionCameras="explicitly labelled establishing views, restored afterwards"});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var lighting=_root.Preferences.StageLighting;
        try
        {
            foreach(var time in new[]{StageTime.Midday,StageTime.Night})
            {
                if(!_headless)await SelectLocalLighting(time);
                foreach(var (stage,signature) in SceneryStages)
                {
                    if(_headless&&time==StageTime.Night)continue;
                    bool practice=stage==BlastStages.CircuitGarden;
                    _root.StartLocal(new SessionSettings(Stage:stage,Bots:practice?0:7,SuddenDeath:false),1,practice:practice);
                    await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"scenery tour enters "+stage,180);
                    await Frames(6);
                    var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
                    if(time==StageTime.Midday){await CheckScenery(stage,terrain,signature);if(!_headless)await SceneryHiddenOnLow(stage);}
                    if(!_headless)CheckFraming(stage,GetWindow().Size);
                    string look=StageLighting.ToWire(time);
                    await Capture($"scenery-{stage}-battle-{look}");
                    if(!_headless&&time==StageTime.Midday)
                    {
                        // A labelled establishing view pulled back from the ordinary camera.
                        var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
                        var (h,v)=(camera.HOffset,camera.VOffset);
                        camera.HOffset=0;camera.VOffset=0;
                        camera.Position=new(terrain.Width*1.18f+7,15,terrain.Depth+15);
                        camera.LookAt(new Vector3(terrain.Width*.45f,0,terrain.Depth*.38f));
                        await Capture($"scenery-{stage}-establishing",6);
                        camera.Transform=pose;camera.HOffset=h;camera.VOffset=v;await Frames(2);
                    }
                    Input(0,default);_root.Back();await Frames(3);
                }
            }
            if(!_headless)await SelectLocalLighting(StageTime.Midday);
            await SceneryRidesFlood();
            await SceneryHiddenInMiniature();
        }
        finally
        {
            Input(0,default);Input(1,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            _root.Navigate("Modes");
            if(!_headless)await SelectLocalLighting(lighting);
        }
    }

    private async Task SceneryHiddenOnLow(string stage)
    {
        var original=_root.Preferences.Graphics;
        try
        {
            await SelectGraphics(GraphicsPreset.Low);await Frames(3);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            Check(terrain.Scenery is {} hidden&&!hidden.IsVisibleInTree(),"Low preset hides decorative scenery: "+stage);
            await Capture($"scenery-{stage}-low");
            await SelectGraphics(GraphicsPreset.High);await Frames(3);
            terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            Check(terrain.Scenery is {} shown&&shown.IsVisibleInTree(),"High preset restores decorative scenery: "+stage);
        }
        finally {await SelectGraphics(original);await Frames(3);}
    }

    private async Task CheckScenery(string stage,TempleStageView terrain,string[] signature)
    {
        var scenery=terrain.Scenery;
        Check(scenery!=null&&scenery.IsVisibleInTree()&&scenery.Name==TempleStageView.SceneryNodeName,"arena has visible decorative scenery: "+stage);
        if(scenery==null)return;
        foreach(var name in signature)
            Check(AuditDescendants(scenery).Any(n=>n.Name.ToString().TrimStart('@').StartsWith(name,StringComparison.Ordinal)),$"{stage} scenery includes {name}");
        Check(!AuditDescendants(scenery).OfType<CollisionObject3D>().Any(),"scenery adds no collision bodies: "+stage);
        var problems=AuditModelProblems(scenery,out int meshes,out int vertices);
        Event("scenery-geometry",new{stage,meshes,vertices,motions=terrain.SceneryMotionCount,problems=problems.Take(5)});
        Check(problems.Count==0&&meshes>=20,$"scenery geometry is finite, nonempty and textured: {stage} meshes={meshes} {string.Join("; ",problems.Take(3))}");
        Check(terrain.SceneryMotionCount>0,"scenery has animated life: "+stage);
        var intrusions=SceneryIntrusions(terrain);
        Check(intrusions.Count==0,$"scenery stays outside every walkable cell's standing volume: {stage} {string.Join(", ",intrusions.Take(6))}");

        // Animated props actually move, and placement still holds mid-animation.
        var tracked=AuditDescendants(scenery).OfType<Node3D>().ToDictionary(n=>n,n=>n.Transform);
        await Frames(24);
        int moved=tracked.Count(p=>GodotObject.IsInstanceValid(p.Key)&&!p.Key.Transform.IsEqualApprox(p.Value));
        Check(moved>0,$"scenery animates over real frames: {stage} moved={moved}");
        intrusions=SceneryIntrusions(terrain);
        Check(intrusions.Count==0,$"animated scenery remains outside play space: {stage} {string.Join(", ",intrusions.Take(6))}");
    }

    /// <summary>Mesh bounds in arena space against each passable cell's floor-to-headroom column.</summary>
    private static List<string> SceneryIntrusions(TempleStageView terrain)
    {
        var layout=terrain.Layout;var inverse=terrain.GlobalTransform.AffineInverse();var hits=new List<string>();
        const float slack=.08f,headroom=2.1f;
        foreach(var geometry in AuditDescendants(terrain.Scenery!).OfType<GeometryInstance3D>())
        {
            Aabb? local=geometry switch
            {
                MeshInstance3D {Mesh:{} mesh}=>mesh.GetAabb(),
                MultiMeshInstance3D {Multimesh:{} multimesh}=>multimesh.GetAabb(),
                _=>null
            };
            if(local==null||!geometry.IsVisibleInTree())continue;
            var bounds=inverse*geometry.GlobalTransform*local.Value;
            int x0=Math.Max(0,Mathf.FloorToInt(bounds.Position.X+slack)),x1=Math.Min(layout.Width-1,Mathf.FloorToInt(bounds.End.X-slack));
            int z0=Math.Max(0,Mathf.FloorToInt(bounds.Position.Z+slack)),z1=Math.Min(layout.Height-1,Mathf.FloorToInt(bounds.End.Z-slack));
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                foreach(var cell in new[]{layout.At(x,z),layout.At(x,z).Below})
                {
                    if(cell is not {IsPassable:true})continue;
                    bool ramp=cell.Kind==CellKind.Ramp;
                    float floor=(ramp?Math.Min(cell.RampFromTier,cell.RampToTier):cell.Tier)*TempleStageView.HeightScale;
                    float top=(ramp?Math.Max(cell.RampFromTier,cell.RampToTier):cell.Tier)*TempleStageView.HeightScale+headroom;
                    if(bounds.End.Y>floor+.02f&&bounds.Position.Y<top){hits.Add($"{geometry.GetParent().Name}/{geometry.Name}@{x},{z}");goto next;}
                }
            next:;
        }
        return hits;
    }

    /// <summary>A real timed flood lifts floating props with the rendered water.</summary>
    private async Task SceneryRidesFlood()
    {
        _root.StartLocal(new SessionSettings(Stage:BlastStages.SunkenTemple,Bots:0,Wins:3,RoundSeconds:60,SuddenDeath:true,Ghosts:false),2);
        Input(1,default);
        await Frames(10);
        var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
        var boat=AuditDescendants(terrain.Scenery!).OfType<Node3D>().First(n=>n.Name.ToString().TrimStart('@').StartsWith("Sailboat",StringComparison.Ordinal));
        float before=boat.Position.Y;
        Check(Math.Abs(before-terrain.WaterHeight)<.15f,$"sailboat floats on the calm sea: boat={before:0.00} water={terrain.WaterHeight:0.00}");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"scenery flood enters hurry",180);
        await MoveTo(12.5f,2.5f,200);
        await WaitState(s=>s.GetProperty("waterLevel").GetSingle()>=.25f,"scenery flood covers courtyard",3000);
        await Frames(30);
        Check(boat.Position.Y>before+.25f&&Math.Abs(boat.Position.Y-terrain.WaterHeight)<.15f,
            $"sailboat rides the rising flood: before={before:0.00} after={boat.Position.Y:0.00} water={terrain.WaterHeight:0.00}");
        await Capture("scenery-sunken-temple-flood");
        Input(0,default);_root.Back();await Frames(3);
    }

    /// <summary>The setup miniature keeps its plinth readable: scenery stays hidden there.</summary>
    private async Task SceneryHiddenInMiniature()
    {
        _root.Navigate("Setup");await Frames(8);
        var state=_root.FindChild("SetupSettings",true,false)!;
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
        var seen=new HashSet<string>();
        for(int i=0;i<10&&seen.Add(menu.Settings.Stage);i++)
        {
            await Frames(4);
            var miniature=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama"&&t.IsVisibleInTree());
            Check(miniature.Scenery==null,"menu miniature skips arena scenery: "+menu.Settings.Stage);
            await KeyPress(Key.Right);
        }
        Check(seen.Count>=6,"menu miniature check visited every selectable arena: "+string.Join(',',seen));
        await Capture("scenery-menu-miniature");
        _root.Navigate("Modes");await Frames(3);
    }
}
