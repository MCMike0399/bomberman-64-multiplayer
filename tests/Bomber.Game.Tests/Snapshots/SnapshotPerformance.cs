using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bomber.Game.Matches;

internal static class SnapshotPerformance
{
    public static void Run(string[] args)
    {
        string output=args.First(a=>a.StartsWith("--output="))[9..];var rows=new List<object>();
        bool eight=args.Contains("--eight-player");int humans=eight?1:3,bots=eight?7:3;string difficulty=eight?"hard":"normal";
        string[] stages=eight?["sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"]:["sunken-temple"];
        foreach(string stage in stages)foreach(int seed in new[]{64,9876,1337})for(int repeat=0;repeat<2;repeat++)
        {
            var match=new LocalMatch(new MatchOptions{HumanPlayers=humans,BotPlayers=bots,BotDifficulty=difficulty,Seed=seed,Stage=stage,Wins=5,RoundSeconds=120});
            // Legacy comparisons include text encoding inside Tick, even though
            // actual local presentation now consumes structured messages.
            match.Message+=static _=>{};
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var times=new List<double>();long allocated=0,bytes=0,eventBytes=0;int frames=0,maxBytes=0,ticks=0;string? previous=null;
            for(int tick=0;tick<2700&&!match.IsEnded;tick++)
            {
                for(int seat=0;seat<humans;seat++)
                {int direction=(tick/45+seat)%4;match.SetInput(seat,new(direction==0?1:direction==2?-1:0,direction==1?1:direction==3?-1:0,tick%50==0));}
                long before=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();match.Tick();double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if(tick>=300){times.Add(ms);allocated+=GC.GetAllocatedBytesForCurrentThread()-before;}ticks++;
                if(match.StateJson is not {} json||ReferenceEquals(previous,json))continue;previous=json;
                using var doc=JsonDocument.Parse(json);var state=doc.RootElement.GetProperty("d");
                foreach(var property in state.EnumerateObject())if(property.Name is not ("events" or "serverTime"))digest.AppendData(Encoding.UTF8.GetBytes(property.Value.GetRawText()));
                if(tick<300)continue;
                int size=Encoding.UTF8.GetByteCount(json);frames++;bytes+=size;maxBytes=Math.Max(maxBytes,size);eventBytes+=Encoding.UTF8.GetByteCount(state.GetProperty("events").GetRawText());
            }
            var sorted=times.Order().ToArray();double Q(double q)=>sorted[(int)Math.Ceiling((sorted.Length-1)*q)];
            rows.Add(new{stage,seed,repeat,ticks,frames,authoritySha256=Convert.ToHexStringLower(digest.GetHashAndReset()),tickMs=new{mean=times.Average(),p50=Q(.5),p95=Q(.95),p99=Q(.99),max=Q(1)},allocatedBytesPerTick=allocated/(double)times.Count,snapshotBytes=bytes/(double)frames,eventBytes=eventBytes/(double)frames,maxSnapshotBytes=maxBytes});
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{schemaVersion=1,humans,bots,difficulty,warmupTicks=300,
            scope="Authority plus legacy text message encoding; inspection and hashing excluded",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS snapshot performance measurement: "+output);
    }
}
