using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bomber.Game.Simulation;

// Stock Pyramid: ordinary slot-zero human vs slot-one bot, without moving spawns/cover/items.
static class StockHumanPressureChecks
{
    internal sealed record Result(string Stage,string Difficulty,int Seed,int ActiveTicks,int EligibleBotTicks,
        int Bombs,int HumanPressureBombs,int FirstHumanPressureTick,int LongestHumanPressureGapTicks,
        int BotDeaths,int BotPickups,int ItemsRevealed,int InitialCover,string InitialCoverFingerprint,int CoverDestroyed,float FinalDistanceToHuman);
    public static int Run(string[] args)
    {
        var results=new List<Result>();
        foreach(string level in new[]{"medium","hard"})
        foreach(int seed in new[]{64,9876})results.Add(Measure(level,seed));
        string json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
        string? output=args.FirstOrDefault(a=>a.StartsWith("--output="))?[9..];
        if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json+"\n");}
        if(args.Contains("--stock-human-pressure"))Console.WriteLine(json);
        // Wider stock Pyramid includes a longer opening-crate route: allow eight seconds
        // for first pressure while retaining five threats, bounded gaps and no self-deaths. Rich regional loot
        // now permits a farming detour; larger early blasts also need longer escapes.
        if(!args.Contains("--measure-only"))
            foreach(var r in results)
                if(r.ActiveTicks!=900||r.HumanPressureBombs<5||r.FirstHumanPressureTick<0||r.FirstHumanPressureTick>240||r.LongestHumanPressureGapTicks>240||r.BotDeaths>0)
                    throw new Exception($"Passive/unsafe stock Pyramid {r.Difficulty} seed={r.Seed}: pressure={r.HumanPressureBombs}, first={r.FirstHumanPressureTick}, gap={r.LongestHumanPressureGapTicks}, deaths={r.BotDeaths}");
        Console.WriteLine((args.Contains("--measure-only")?"MEASURE":"PASS")+" 4 stock Pyramid human-facing pressure scenarios");return results.Count;
    }
    static T Field<T>(BlastSession session,string name)=>(T)typeof(BlastSession).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
    static Result Measure(string level,int seed)
    {
        var arena=BlastStages.Get("sunken-temple");var f=new Fixture(arena,2,seed,level);
        f.Session.AiTestAddHuman("cpu-0",0);
        var spawn=arena.Spawns[0];f.Session.AiTestPosition("cpu-0",spawn.X+.5f,spawn.Z+.5f);f.Step(61);
        var human=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        var soft=Field<SoftBlockGrid>(f.Session,"_softBlocks");var items=Field<List<BlastItem>>(f.Session,"_items");
        var known=new HashSet<string>();var knownItems=new HashSet<string>(items.Select(i=>i.Id));
        int initialCover=0;for(int x=0;x<arena.Width;x++)for(int z=0;z<arena.Height;z++)if(soft.IsAlive(x,z))initialCover++;
        string coverFingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(soft.ToBase64())));
        int active=0,eligible=0,bombs=0,pressure=0,first=-1,last=0,gap=0,deaths=0,revealed=0;
        long initialPickup=bot.PickupSerial;
        for(int tick=0;tick<900;tick++)
        {
            // Sustain the human target for the bounded observation; the bot remains mortal.
            human.InvincibleTimer=2;human.Input=default;
            if(f.Session.AiTestPhase is not (RoundPhase.Fight or RoundPhase.Hurry))break;
            active++;
            if(bot.Alive&&!bot.Ghost&&bot.CarriedById==null&&bot.StunTimer<=0&&!bot.Airborne&&!bot.Falling&&bot.KickWindupRemaining<=0&&bot.KickRecoveryRemaining<=0)eligible++;
            f.Step();
            foreach(var item in items)if(knownItems.Add(item.Id))revealed++;
            foreach(var bomb in f.Session.AiTestBombs)if(bomb.OwnerId==bot.Id&&known.Add(bomb.Id))
            {
                bombs++;
                if(BlastGeometry.Contains(arena,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,human.X,human.Y,human.Z,bomb.IsRed))
                {pressure++;if(first<0)first=tick;gap=Math.Max(gap,tick-last);last=tick;}
            }
            if(!bot.Alive){deaths++;Console.WriteLine("DEATH_TRACE "+level+" "+seed+" "+JsonSerializer.Serialize(Field<object>(f.Session,"_presentationEvents")));break;}
        }
        gap=Math.Max(gap,active-last);
        int cover=0;for(int x=0;x<arena.Width;x++)for(int z=0;z<arena.Height;z++)if(soft.IsAlive(x,z))cover++;
        float distance=MathF.Sqrt((human.X-bot.X)*(human.X-bot.X)+(human.Y-bot.Y)*(human.Y-bot.Y)+(human.Z-bot.Z)*(human.Z-bot.Z));
        return new(arena.Id,level,seed,active,eligible,bombs,pressure,first,gap,deaths,(int)(bot.PickupSerial-initialPickup),revealed,initialCover,coverFingerprint,initialCover-cover,distance);
    }
}
