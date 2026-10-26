using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

internal static class LocalPerformance
{
    public static async Task Run(string[] args)
    {
        string output=args.First(a=>a.StartsWith("--output="))[9..];var rows=new List<object>();
        foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"})
        foreach(int seed in new[]{64,9876,1337})for(int repeat=0;repeat<2;repeat++)
        {
            await using var session=new LocalSession(new SessionSettings(Stage:stage,Bots:7,BotDifficulty:"hard",Wins:5,RoundSeconds:120),seed:seed);
            session.DrainMessages();using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var times=new List<double>();long allocated=0;int frames=0;
            for(int tick=0;tick<2700&&!session.IsEnded;tick++)
            {
                int direction=tick/45%4;
                long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                session.SendInput(0,new PlayerInput(direction==0?1:direction==2?-1:0,direction==1?1:direction==3?-1:0,tick%50==0));
                session.Advance(1d/30);JsonElement? state=null;
                foreach(var message in session.DrainMessages())
                    if(message.GetProperty("t").GetString()=="state")state=message.GetProperty("d").Clone();
                double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long delta=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(tick>=300){times.Add(ms);allocated+=delta;}
                if(state is not {} snapshot)continue;
                frames++;
                foreach(var property in snapshot.EnumerateObject())if(property.Name is not ("events" or "serverTime"))
                    digest.AppendData(Encoding.UTF8.GetBytes(property.Value.GetRawText()));
            }
            var sorted=times.Order().ToArray();double Q(double q)=>sorted[(int)Math.Ceiling((sorted.Length-1)*q)];
            rows.Add(new{stage,seed,repeat,frames,authoritySha256=Convert.ToHexStringLower(digest.GetHashAndReset()),
                tickMs=new{mean=times.Average(),p50=Q(.5),p95=Q(.95),p99=Q(.99),max=Q(1)},allocatedBytesPerTick=allocated/(double)times.Count});
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{schemaVersion=1,humans=1,bots=7,difficulty="hard",warmupTicks=300,
            scope="LocalSession input, shared authority, snapshot delivery and retained presentation state; hashing excluded",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS local session performance measurement: "+output);
    }
}
