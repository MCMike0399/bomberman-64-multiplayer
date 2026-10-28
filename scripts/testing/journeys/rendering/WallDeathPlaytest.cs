using Godot;
using Bomber.Game.Matches;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    /// <summary>Idle real players meet both closing walls; inspect the cosmetic stand-ins without changing authority.</summary>
    private async Task WallDeath()
    {
        Event("wall-death-policy",new {ordinarySession=true,stateMutation=false,inspectionCamera=true});
        var battle=_root.GetViewport().GetCamera3D()!;
        var camera=new Camera3D {Fov=34,Near=.05f,Far=100};_root.AddChild(camera);
        try
        {
            foreach(string stage in new[]{BlastStages.GreedyTrap,BlastStages.TopRules})
            {
                _root.StartLocal(new SessionSettings(Stage:stage,Bots:0,RoundSeconds:60,Ghosts:false),4);
                await WaitState(s=>s.GetProperty("phase").GetInt32() is 1 or 2,"wall death starts real battle: "+stage,180);
                await WaitState(s=>!s.GetProperty("players")[0].GetProperty("alive").GetBoolean(),"real closure eliminates idle player: "+stage,2400);
                var effects=AuditDescendants(LightingArena).OfType<CrushEffect>().ToArray();
                Check(effects.Length>=2,"both closing sides spawn crush stand-ins: "+stage);
                Check(State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="columns"),"authority identifies closing columns as death cause: "+stage);
                Check(effects.Any(e=>e.Position.X<9.5f)&&effects.Any(e=>e.Position.X>9.5f),"both left and right walls retain a visible victim: "+stage);
                Check(!AuditDescendants(LightingArena).OfType<BurnoutEffect>().Any(),"wall deaths never spawn burning victims: "+stage);
                if(effects.Length==0)continue;
                var effect=effects.OrderBy(e=>e.Position.X).First();
                await Capture("wall-death-"+stage+"-battle",0);
                // Pause only the presentation stand-in, retaining real authority and arena geometry.
                foreach(var e in effects)e.ProcessMode=ProcessModeEnum.Disabled;
                async Task At(float age,string label)
                {
                    effect._Process(Math.Max(0,age-effect.Age));
                    var target=effect.GlobalPosition+new Vector3(0,.85f,0);
                    camera.Position=target+new Vector3(2,2.3f,4.5f);camera.LookAt(target);camera.MakeCurrent();
                    await Capture("wall-death-"+stage+"-"+label,0);
                }
                await At(.12f,"squeeze");await At(.65f,"flat");
                Check(effect.Puppet.SquintEyes&&!effect.Puppet.Charred,"crush reuses squeezed eyes with original suit: "+stage);
                Check(effect.PaperScale.Z<.15f,"crush becomes paper-flat: "+stage);
                Check(effect.Position.X>State.GetProperty("closedLeft").GetInt32(),"stand-in clears opaque wall face: "+stage);
                await At(CrushEffect.PuffAt+.2f,"puff");
                foreach(var e in effects)e.ProcessMode=ProcessModeEnum.Inherit;
                battle.MakeCurrent();
                await Frames(65);
                Check(!GodotObject.IsInstanceValid(effect),"crush effect cleans itself up: "+stage);
                _root.Back();await Frames(3);
            }
            // A fixed strip makes the timing beats readable together under the real arena lighting.
            _root.StartLocal(new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:0,SuddenDeath:false),2);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"crush strip starts real arena",180);
            var strip=new Node3D();LightingArena.AddChild(strip);
            float[] ages=[.08f,.18f,.65f,1.1f,1.48f];
            for(int i=0;i<ages.Length;i++)
            {
                var e=new CrushEffect(new ArtMaterials(),ArtMaterials.PlayerColors[3],true){Position=new(5.5f+i*1.8f,2.7f,2.5f),ProcessMode=ProcessModeEnum.Disabled};
                strip.AddChild(e);e._Process(ages[i]);
            }
            var center=new Vector3(9.1f,3.55f,2.5f);camera.Position=center+new Vector3(0,3.8f,11);camera.LookAt(center);camera.MakeCurrent();
            await Capture("wall-death-pose-strip");strip.QueueFree();
        }
        finally
        {
            battle.MakeCurrent();camera.QueueFree();
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        }
    }
}
