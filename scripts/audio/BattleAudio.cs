using Godot;
namespace BlastCircuit.Audio;

/// <summary>Cached original cues with local volume and bounded mix headroom; no sampled game audio.</summary>
public partial class BattleAudio : Node
{
    private const int VoiceLimit=10;
    private readonly AudioStreamPlayer[] _voices=new AudioStreamPlayer[VoiceLimit];
    private readonly Dictionary<string,AudioStreamWav> _streams=new();
    private readonly Dictionary<string,int> _counts=new();
    private int _voice,_startsThisFrame;
    private ulong _lastFrame=ulong.MaxValue;
    private float _volume=.8f;
    public float Volume {get=>_volume;set{_volume=float.IsFinite(value)?Math.Clamp(value,0,1):0;UpdateVolume();}}
    public IReadOnlyDictionary<string,int> Counts=>_counts;
    public IReadOnlyDictionary<string,SynthClip> GeneratedClips=>SoundSynth.GenerateAll();
    public int VoicePlayCount {get;private set;}
    public int DroppedCueCount {get;private set;}
    public string? LastPlayedClip {get;private set;}

    public override void _Ready()
    {
        for(var i=0;i<_voices.Length;i++){_voices[i]=new AudioStreamPlayer();AddChild(_voices[i]);}
        foreach(var (name,clip) in GeneratedClips)
            _streams[name]=new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=clip.SampleRate,Stereo=false,Data=clip.Pcm16};
        UpdateVolume();
    }
    public void Bomb()=>Play("bomb");
    public void Blast(float radius=1.3f)=>Play(radius<=1.6f?"blast-small":radius<=2.7f?"blast-medium":"blast-large","blast");
    public void Pickup()=>Play("pickup");
    public void Pump(int charge)=>Play("pump-"+Math.Clamp(charge,1,3),"pump");
    public void Hurry()=>Play("hurry");
    public void Kick()=>Play("kick");
    public void Lift()=>Play("lift");
    public void Throw(float strength=1)=>Play("throw",pitch:.9f+Math.Clamp(float.IsFinite(strength)?strength:1,0,1)*.15f);
    public void Death()=>Play("death");
    public void Splash()=>Play("splash");
    public void RoundReady()=>Play("round-ready");
    public void Fight()=>Play("fight");
    public void RoundWin()=>Play("round-win");
    public void MenuConfirm()=>Play("menu-confirm");
    public void MenuBack()=>Play("menu-back");
    public void StopAll(){foreach(var voice in _voices)if(voice is not null)voice.Stop();}

    private void Play(string clip,string? cue=null,float pitch=1)
    {
        if(!_streams.TryGetValue(clip,out var stream))return;
        var frame=Engine.GetProcessFrames();
        if(frame!=_lastFrame){_lastFrame=frame;_startsThisFrame=0;}
        if(_startsThisFrame>=VoiceLimit){DroppedCueCount++;return;}
        _startsThisFrame++;
        // Prefer a free voice. At capacity recycle in rotation instead of growing the node tree.
        var index=_voice++%VoiceLimit;
        for(var offset=0;offset<VoiceLimit;offset++)
        {
            var candidate=(index+offset)%VoiceLimit;
            if(!_voices[candidate].Playing){index=candidate;break;}
        }
        var voice=_voices[index];voice.Stream=stream;voice.PitchScale=pitch;voice.Play();
        VoicePlayCount++;LastPlayedClip=clip;
        var key=cue??clip;_counts[key]=_counts.GetValueOrDefault(key)+1;
    }
    private void UpdateVolume()
    {
        // Ten coincident normalized peaks remain below unity even at Volume=1.
        var db=_volume<=0?-80:-22+Mathf.LinearToDb(_volume);
        foreach(var voice in _voices)if(voice is not null)voice.VolumeDb=db;
    }
}
