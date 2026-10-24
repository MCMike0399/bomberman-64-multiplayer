using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
using BlastCircuit.Audio;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private JsonElement Presentation=>JsonSerializer.SerializeToElement(_root.PresentationState);
    private int Cue(string name)=>_root.BattleAudio.Counts.GetValueOrDefault(name);
    private async Task PresentationEffects()
    {
        AudioEffectCapture? capture=null;int effectIndex=-1;
        if(!_headless){capture=new AudioEffectCapture {BufferLength=30};effectIndex=AudioServer.GetBusEffectCount(0);AudioServer.AddBusEffect(0,capture,effectIndex);}
        try
        {
            foreach(var (pumps,tier) in new[]{(0,"small"),(1,"medium"),(3,"large")})
            {
                _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"effect practice reaches fight",150);
                int before=Cue("blast"),pumpBefore=Cue("pump"),throwBefore=Cue("throw");
                if(pumps==0){Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);}
                else
                {
                    Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);Input(0,new PlayerInput(0,0,B:true));await Frames(2);
                    for(int i=0;i<pumps;i++){Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);Input(0,new PlayerInput(0,0,B:true));await Frames(14);}
                    Input(0,default);await Frames(3);
                    Check(Cue("pump")-pumpBefore==pumps,"accepted pumps each play once: "+tier);
                    Check(Cue("throw")==throwBefore+1,"accepted throw plays once: "+tier);
                }
                await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"real "+tier+" bomb detonates",130);
                Check(Cue("blast")==before+1,"blast cue starts once: "+tier);
                var fx=Presentation.GetProperty("explosions").EnumerateArray().Single(x=>x.GetProperty("tier").GetString()==tier);
                Check(Math.Abs(fx.GetProperty("radius").GetSingle()-State.GetProperty("hazards")[0].GetProperty("r").GetSingle())<.001f,"visual uses authority radius: "+tier);
                int lobes=fx.GetProperty("lobes").GetInt32();Check(lobes==(tier=="small"?6:tier=="medium"?10:14),"blast tier has distinct billow construction: "+tier);
                Event("blast-visual",new {tier,fx});await Capture("blast-"+tier+"-ignition",0);
                await Frames(3);await Capture("blast-"+tier+"-bloom",0);
                await Frames(8);
                Check(Cue("blast")==before+1,"repeated history never replays blast: "+tier);
                if(tier=="large")
                {
                    await WaitState(_=>Presentation.GetProperty("explosions").EnumerateArray().Any(x=>x.GetProperty("phase").GetString()=="vapor"),"large blast reaches its dissipating phase",12);
                    Check(Presentation.GetProperty("explosions").EnumerateArray().Any(x=>x.GetProperty("phase").GetString()=="vapor"&&!x.GetProperty("hazard").GetBoolean()),"large blast has a nonhazardous dissipating phase");
                    await Capture("blast-large-vapor",0);
                }
                await Frames(35);Check(Presentation.GetProperty("explosions").GetArrayLength()==0,"finished effects release nodes: "+tier);
                _root.Back();await Frames(3);
            }
            Check(Cue("death")>0,"accepted self-explosion produces death cue even with immediate practice respawn");
            _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"kick/lift practice reaches fight",150);
            int lifts=Cue("lift"),throws=Cue("throw"),kicks=Cue("kick");
            Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
            Input(0,new PlayerInput(0,0,B:true));await Frames(3);Check(Cue("lift")==lifts+1,"lifting a ground bomb plays lift cue");
            Input(0,default);await Frames(3);Check(Cue("throw")==throws+1,"releasing carried bomb plays throw cue");
            await Capture("action-lift-throw");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"kick practice reaches fight",150);
            // Set the facing before a near-foot drop, then deliberately kick with a fresh A edge.
            Input(0,new PlayerInput(1,0));await Frames(1);Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(7);
            Input(0,new PlayerInput(0,0,A:true));await WaitState(_=>Cue("kick")>kicks,"second primary press accepts a real kick",25);Input(0,default);await Frames(2);
            Check(Cue("kick")>kicks,"actual contact kick plays its own cue");await Capture("action-kick");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"water fall practice reaches fight",150);
            int splashes=Cue("splash");Input(0,new PlayerInput(-1,0));
            for(int i=0;i<140&&Cue("splash")==splashes;i++)await Frames(1);
            Input(0,default);Check(Cue("splash")>splashes,"falling off the island produces water cue and ripple effect");await Capture("action-water-splash",0);_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Bots:7),1);await Frames(6);
            Check(State.GetProperty("players").GetArrayLength()==8,"new clock/head/win strip supports eight participants");await Capture("hud-eight-players");
            if(capture!=null)
            {
                await Task.Delay(250);await Frames(2);
                int available=capture.GetFramesAvailable();var samples=capture.GetBuffer(available);
                double sum=0,peak=0;foreach(var sample in samples){sum+=sample.X*sample.X+sample.Y*sample.Y;peak=Math.Max(peak,Math.Max(Math.Abs(sample.X),Math.Abs(sample.Y)));}
                double rms=Math.Sqrt(sum/Math.Max(1,samples.Length*2));
                Check(samples.Length>1000&&rms>.0001,"actual engine mixer produces non-silent action audio");Check(peak<.99,"recorded action mix stays below clipping");
                Event("audio-mix",new {frames=samples.Length,peak,rms,discarded=capture.GetDiscardedFrames(),cues=_root.BattleAudio.Counts});
                SaveMixedAudio(samples,AudioServer.GetMixRate());
            }
            else Event("audio-mix-unmeasured",new {reason="headless; deterministic PCM synthesis checked separately"});
            _root.Back();await Frames(3);
        }
        finally{if(effectIndex>=0)AudioServer.RemoveBusEffect(0,effectIndex);}
    }
    private void SaveMixedAudio(Vector2[] samples,float rate)
    {
        using var file=File.Create(Path.Combine(_output,"actions-mixed.wav"));using var writer=new BinaryWriter(file);
        int size=samples.Length*4;writer.Write("RIFF"u8);writer.Write(size+36);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write((int)rate);writer.Write((int)rate*4);writer.Write((short)4);writer.Write((short)16);writer.Write("data"u8);writer.Write(size);
        foreach(var sample in samples){writer.Write((short)(Math.Clamp(sample.X,-1,1)*32767));writer.Write((short)(Math.Clamp(sample.Y,-1,1)*32767));}
    }
}
