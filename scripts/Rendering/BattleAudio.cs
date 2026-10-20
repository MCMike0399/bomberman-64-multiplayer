using Godot;
namespace BlastCircuit.Rendering;

/// <summary>Small original synthesized cues; no external samples or copyrighted audio.</summary>
public partial class BattleAudio : Node
{
    private readonly AudioStreamPlayer[] _voices=new AudioStreamPlayer[6];
    private AudioStreamWav _bomb=null!,_blast=null!,_pickup=null!,_pump=null!,_hurry=null!;
    private int _voice;
    public override void _Ready()
    {
        for(int i=0;i<_voices.Length;i++){_voices[i]=new AudioStreamPlayer {VolumeDb=-15};AddChild(_voices[i]);}
        _bomb=Tone(.1,220,false);_blast=Tone(.38,70,true);_pickup=Tone(.17,740,false);_pump=Tone(.12,290,false);_hurry=Tone(.55,170,false);
    }
    public void Bomb()=>Play(_bomb);
    public void Blast(float radius=1.3f)=>Play(_blast,Mathf.Clamp(1.15f-radius*.08f,.65f,1.1f));
    public void Pickup()=>Play(_pickup);
    public void Pump(int charge)=>Play(_pump,1+charge*.17f);
    public void Hurry()=>Play(_hurry,.85f);
    private void Play(AudioStream stream,float pitch=1){var voice=_voices[_voice++%_voices.Length];voice.Stream=stream;voice.PitchScale=pitch;voice.Play();}
    private static AudioStreamWav Tone(double seconds,double frequency,bool noise)
    {
        const int rate=22050;int count=(int)(rate*seconds);var data=new byte[count*2];var rng=new Random(64);
        for(int i=0;i<count;i++)
        {
            double t=(double)i/rate,fade=Math.Pow(1-(double)i/count,2);
            double sample=Math.Sin(t*Math.Tau*frequency*(1-t*.8))*(noise?.4:.7)+(noise?(rng.NextDouble()*2-1)*.45:0);
            short value=(short)(sample*fade*22000);data[i*2]=(byte)(value&255);data[i*2+1]=(byte)((value>>8)&255);
        }
        return new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Stereo=false,Data=data};
    }
}
