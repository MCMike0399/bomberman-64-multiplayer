using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task CopperLockCombatTour()
    {
        Check(!_headless,"Copper Lock colored combat review requires actual rendered frames");
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:7,SuddenDeath:false,RoundSeconds:300),1,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player colored Copper Lock battle starts",180);
            bool bomb=false,blast=false;
            for(int n=0;n<330&&_root.ScreenName=="Match";n++)
            {
                bomb|=State.GetProperty("bombs").GetArrayLength()>0;
                blast|=State.GetProperty("hazards").GetArrayLength()>0;
                if(bomb&&blast)break;
                await Frames(1);
            }
            Check(bomb&&blast&&State.GetProperty("players").GetArrayLength()==8,"real eight-player AI combat produces accepted bombs and blasts");
            CheckFraming(BlastStages.InTheGutter,GetWindow().Size);
            await Capture("copper-lock-finish-high-battle");
            await SelectGraphics(GraphicsPreset.Low);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            Check(terrain.GetNode<MeshInstance3D>("CopperLockOuterWater").IsVisibleInTree(),"Low keeps the connected channel's outer water");
            Check(terrain.FindChild("CopperLockWestMachine",true,false) is Node3D left&&left.IsVisibleInTree()&&
                terrain.FindChild("CopperLockEastMachine",true,false) is Node3D right&&right.IsVisibleInTree(),"Low retains both hero sluice machines");
            await Capture("copper-lock-finish-low-battle");
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Night);
            await Capture("copper-lock-finish-night-battle");
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            camera.Position=new(33,23,35);camera.LookAt(new Vector3(10.5f,1.3f,10.5f));
            await Capture("copper-lock-finish-oblique");camera.Transform=pose;
            Event("copper-lock-real-combat",new{seed=64,bomb,blast,round=State.GetProperty("round").GetInt32(),
                alive=State.GetProperty("players").EnumerateArray().Count(p=>p.GetProperty("alive").GetBoolean()),participants=8});
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            await SelectGraphics(quality);await SelectLocalLighting(lighting);
        }
    }

    private async Task CopperLockCurrentChange()
    {
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            if(!_headless){await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);}
            // Seed selection is the existing local QA seam, not state mutation.
            // Seed 64 changes from westward to eastward during its second cycle.
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false,RoundSeconds:300)
                {Advanced=new AdvancedRules{BlockPercent=0}},4,seed:64);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"current witness starts an ordinary seeded match",180);
            await WaitState(s=>s.GetProperty("gutter").GetProperty("x").GetSingle()<-.5f,"accepted current initially flows west",120);
            await Frames(30);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var fish=AuditDescendants(terrain).OfType<Node3D>().Single(n=>n.Name=="GutterCurrentMarker");
            var gear=AuditDescendants(terrain).OfType<Node3D>().First(n=>n.Name=="CopperLockDriveGear");
            var leaf=AuditDescendants(terrain).OfType<Node3D>().Where(n=>n.Name.ToString().StartsWith("CopperLockDriftingLeaf")).Last();
            Check(fish.Basis.X.X<-.8f,"fish weathervane visibly faces the accepted westward current");
            var beforeGear=gear.Basis;float before=State.GetProperty("gutter").GetProperty("x").GetSingle();
            await Frames(12);
            Check(beforeGear.X.Cross(gear.Basis.X).Z<-.025f,"drive gear turns continuously west while the accepted current runs west");
            Check(AuditDescendants(terrain).OfType<Node3D>().Count(n=>n.Name=="CopperLockPressureDial")==1,"one active gantry owns the large round pressure dial");
            await Capture("copper-lock-current-west");
            await WaitState(s=>s.GetProperty("gutter").GetProperty("x").GetSingle()>.5f,"unmodified authority changes flow east",300);
            float atChange=leaf.Position.X;await Frames(30);
            float after=State.GetProperty("gutter").GetProperty("x").GetSingle();
            var eastStart=gear.Basis;await Frames(12);
            Check(eastStart.X.Cross(gear.Basis.X).Z>.025f,"the same gear reverses continuously east with the accepted current");
            Check(fish.Basis.X.X>.8f,"fish weathervane swings east after the accepted direction change");
            Check(!gear.Basis.IsEqualApprox(beforeGear),"the machine gear ticks in response to the real direction change");
            Check(leaf.Position.X>atChange+.15f,"sparse floating leaves advect east with the accepted current");
            Check(terrain.GutterWakeMaterial!.GetShaderParameter("current").AsSingle()>.5f,"soft wakes receive the new accepted flow");
            await Capture("copper-lock-current-east");
            Event("copper-lock-current-change",new{seed=64,before,after,weatherTime=State.GetProperty("gutter").GetProperty("time").GetSingle(),
                fishX=fish.Basis.X.X,leafDrift=leaf.Position.X-atChange,ordinaryAuthority=true,stateMutation=false});
        }
        finally
        {
            Input(0,default);_root.Navigate("Modes");await Frames(3);
            if(!_headless){await SelectGraphics(quality);await SelectLocalLighting(lighting);}
        }
    }
}
