using BlastCircuit.Audio;
using System.Security.Cryptography;

var clips=SoundSynth.GenerateAll();
Assert(clips.Count==19,"Missing designed cue variants.");
Assert(ReferenceEquals(clips,SoundSynth.GenerateAll()),"PCM is rebuilt per event.");
foreach(var (name,clip) in clips)
{
    Assert(clip.SampleRate==44100&&clip.Pcm16.Length>8000,name+" has invalid format/duration.");
    Assert(clip.Pcm16.Length%2==0&&Math.Abs(clip.Pcm16.Length/2d/clip.SampleRate-clip.DurationSeconds)<.001,name+" has invalid duration metadata.");
    Assert(clip.Peak is >.7 and <.9&&clip.Rms is >.01 and <.6,name+" is silent or clipping.");
    Assert(clip.Pcm16[0]==0&&clip.Pcm16[1]==0&&clip.Pcm16[^1]==0&&clip.Pcm16[^2]==0,name+" has nonzero playback edges.");
}
Assert(clips.Values.Select(c=>Convert.ToHexString(SHA256.HashData(c.Pcm16))).Distinct().Count()==clips.Count,"Two cues share identical samples.");
var small=clips["blast-small"];var medium=clips["blast-medium"];var large=clips["blast-large"];
Assert(small.DurationSeconds<medium.DurationSeconds&&medium.DurationSeconds<large.DurationSeconds,"Blast body/tail does not scale.");
Assert(LateEnergy(large)>.08&&LateEnergy(large)>LateEnergy(small)*2,$"Large blast lacks its independent longer decay: {LateEnergy(small)} {LateEnergy(medium)} {LateEnergy(large)}");
Assert(large.Peak*Math.Pow(10,-22d/20)*10<1,"Ten coherent voices could clip.");
Console.WriteLine($"PASS {clips.Count} original PCM cues: bounded peaks, audible energy, clean edges, distinct samples, blast decays, cached resources and mix headroom.");
if(args.Length==2&&args[0]=="--export")
{
    Directory.CreateDirectory(args[1]);
    foreach(var (name,clip) in clips)
    {
        using var writer=new BinaryWriter(File.Create(Path.Combine(args[1],name+".wav")));
        writer.Write("RIFF"u8);writer.Write(36+clip.Pcm16.Length);writer.Write("WAVEfmt "u8);
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate*2);writer.Write((short)2);writer.Write((short)16);
        writer.Write("data"u8);writer.Write(clip.Pcm16.Length);writer.Write(clip.Pcm16);
    }
    Console.WriteLine("Exported original WAV cue gallery: "+Path.GetFullPath(args[1]));
}
static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
static double LateEnergy(SynthClip clip)
{
    double total=0,late=0;
    for(var i=0;i<clip.Pcm16.Length/2;i++)
    {
        var sample=(short)(clip.Pcm16[i*2]|clip.Pcm16[i*2+1]<<8)/32768d;
        total+=sample*sample;if(i>clip.SampleRate*.2)late+=sample*sample;
    }
    return late/total;
}
