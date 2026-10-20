namespace BlastCircuit.Rendering;

/// <summary>Original mono PCM cues. Pure synthesis makes the shipped assets deterministic and inspectable.</summary>
public sealed record SynthClip(int SampleRate, byte[] Pcm16, double DurationSeconds, double Peak, double Rms);

public static class SoundSynth
{
    public const int SampleRate = 44100;
    private static readonly Lazy<IReadOnlyDictionary<string,SynthClip>> Clips = new(CreateClips);
    public static IReadOnlyDictionary<string,SynthClip> GenerateAll() => Clips.Value;

    private static IReadOnlyDictionary<string,SynthClip> CreateClips()
    {
        var clips = new Dictionary<string,SynthClip>();
        Add("bomb", .12, (t,n,l) => .75*Chirp(t,190,-850)*Decay(t,26)+.2*n*Decay(t,70));
        Add("kick", .19, (t,n,l) => .65*Chirp(t,150,-450)*Decay(t,24)+.3*n*Decay(t,95)
            +.28*Note(t,75)*Decay(t,18)+.45*(n-l)*Math.Pow(Math.Sin(Math.PI*t/.19),2)*Decay(t,15));
        Add("lift", .17, (t,n,l) => .65*Chirp(t,330,1600)*Decay(t,14)+.2*l*Decay(t,30));
        for(var charge=1;charge<=3;charge++)
        {
            var c=charge;
            Add("pump-"+c,.19+c*.025,(t,n,l) =>
                (.55*Chirp(t,220+c*65,1100+c*220)+(.07+c*.035)*Chirp(t,440+c*130,2200))*Decay(t,12)
                +.3*l*Math.Exp(-Math.Pow((t-.027)/.021,2)));
        }
        Add("throw", .25, (t,n,l) => (n-l)*.45*Math.Sin(Math.PI*t/.25)*Math.Exp(-t*5)
            +.28*Chirp(t,560,-1550)*Decay(t,13));
        Add("blast-small", .26, (t,n,l) =>
            .62*Chirp(t,125,-260)*Decay(t,19)+.48*(n-l)*Decay(t,35)+.3*l*Decay(t,16));
        Add("blast-medium", .48, (t,n,l) =>
            .78*Chirp(t,90,-100)*Decay(t,12)+.65*l*Decay(t,8)
            +.25*n*Decay(t,24)+.17*n*Math.Exp(-Math.Pow((t-.11)/.045,2)));
        Add("blast-large", .78, (t,n,l) =>
            .8*Chirp(t,62,-37)*Decay(t,5)+.6*l*Decay(t,3.8)
            +.28*n*Decay(t,20)+.22*(n-l)*Math.Pow(Math.Max(0,Math.Sin(t*147)),12)*Decay(t,5)
            +.25*Chirp(Math.Max(0,t-.075),43,0)*Math.Exp(-Math.Pow((t-.13)/.09,2)));
        Add("pickup", .23, (t,n,l) => .65*Sequence(t,.085,660,990)*Decay(t,9)+.14*Note(t,1320)*Decay(t,17));
        Add("death", .49, (t,n,l) => (.65*Sequence(t,.14,540,390,245)+.18*Chirp(t,650,-850))*Decay(t,5));
        Add("splash", .42, (t,n,l) => .6*l*Decay(t,8)+.25*(n-l)*Decay(t,18)
            +.45*Chirp(t,240,-1250)*Decay(t,25)+.22*Note(t,380+80*Math.Sin(t*95))*Math.Exp(-Math.Pow((t-.16)/.06,2)));
        Add("hurry", .6, (t,n,l) => .6*Sequence(t,.1,392,523,392,523,392,523)*Decay(t,3));
        Add("round-ready", .35, (t,n,l) => (.6*Sequence(t,.16,392,523)+.14*Note(t,1046))*Decay(t,6));
        Add("fight", .29, (t,n,l) => (.5*Note(t,784)+.25*Note(t,988)+.18*Note(t,1175))*Decay(t,9)+.08*n*Decay(t,60));
        Add("round-win", .72, (t,n,l) => (.6*Sequence(t,.16,523,659,784,1046)+.16*Note(t,523))*Decay(t,3));
        Add("menu-confirm", .105, (t,n,l) => .7*Chirp(t,720,2900)*Decay(t,28));
        Add("menu-back", .105, (t,n,l) => .7*Chirp(t,650,-2800)*Decay(t,28));
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,SynthClip>(clips);

        void Add(string name,double duration,Func<double,double,double,double> wave)
        {
            // Seed is explicitly stable; the platform's string hash must never affect rendered PCM.
            uint seed=2166136261; foreach(var character in name)seed=(seed^character)*16777619;
            var samples=new double[(int)(SampleRate*duration)];double low=0,mean=0;
            for(var i=0;i<samples.Length;i++)
            {
                seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;
                var noise=seed/(double)uint.MaxValue*2-1;low+=.075*(noise-low);
                var t=i/(double)SampleRate;
                // Smooth both edges, including note switches, and remove DC before normalization.
                var edge=Math.Min(1,t/.002)*Math.Min(1,(duration-t)/.018);
                samples[i]=wave(t,noise,low)*edge;mean+=samples[i];
            }
            mean/=samples.Length;
            double maximum=0;
            for(var i=0;i<samples.Length;i++)
            {
                // Taper the DC correction so the first/last sample stay at zero.
                var edge=Math.Min(1,i/(SampleRate*.002))*Math.Min(1,(samples.Length-1-i)/(SampleRate*.018));
                samples[i]-=mean*edge;maximum=Math.Max(maximum,Math.Abs(samples[i]));
            }
            var pcm=new byte[samples.Length*2];double square=0,peak=0;
            for(var i=0;i<samples.Length;i++)
            {
                var value=(short)Math.Round(samples[i]/Math.Max(maximum,1e-9)*.82*32767);
                pcm[i*2]=(byte)(value&255);pcm[i*2+1]=(byte)((value>>8)&255);
                var normalized=value/32768d;square+=normalized*normalized;peak=Math.Max(peak,Math.Abs(normalized));
            }
            // Explicit zeros prevent clicks when a player is recycled after the clip finishes.
            pcm[0]=pcm[1]=pcm[^1]=pcm[^2]=0;
            clips[name]=new SynthClip(SampleRate,pcm,duration,peak,Math.Sqrt(square/samples.Length));
        }
    }
    private static double Sequence(double t,double step,params double[] notes)
    {
        var index=Math.Min((int)(t/step),notes.Length-1);
        var local=t-index*step;
        var edge=Math.Min(1,local/.003)*(index<notes.Length-1?Math.Clamp((step-local)/.007,0,1):1);
        return Note(local,notes[index])*edge;
    }
    private static double Note(double t,double frequency)=>Math.Sin(Math.Tau*t*frequency);
    private static double Chirp(double t,double frequency,double slope)=>Math.Sin(Math.Tau*(frequency*t+.5*slope*t*t));
    private static double Decay(double t,double rate)=>Math.Exp(-t*rate);
}
