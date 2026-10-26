using System.Text.Json;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

// Fixed visible-opponent fixtures isolate pursuit from random item drops and cover clearing.
static class PressureChecks
{
    internal sealed record Result(string Difficulty,int Seed,bool Moving,int ActiveTicks,int InventoryAvailableTicks,int Bombs,int PressureBombs,int FirstPressureTick,int LongestPressureGapTicks,int BotDeaths);
    public static int Run(string[] args)
    {
        var results=new List<Result>();
        foreach(string level in new[]{"easy","medium","hard"})
        foreach(int seed in new[]{64,9876})
        foreach(bool moving in new[]{false,true})results.Add(Measure(level,seed,moving));
        string json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
        string? output=args.FirstOrDefault(a=>a.StartsWith("--output="))?[9..];
        if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json+"\n");}
        if(args.Contains("--pressure"))Console.WriteLine(json);
        if(!args.Contains("--measure-only"))
            foreach(var r in results)
            {
                bool easy=r.Difficulty=="easy";
                if(r.ActiveTicks!=900||r.FirstPressureTick<0||r.FirstPressureTick>(easy?360:120)||r.PressureBombs<(easy?1:6)||r.LongestPressureGapTicks>(easy?600:180)||r.BotDeaths>0)
                    throw new Exception($"Excessively passive/unsafe {r.Difficulty} pursuit moving={r.Moving}: first={r.FirstPressureTick}, pressure={r.PressureBombs}, longestGap={r.LongestPressureGapTicks}, deaths={r.BotDeaths}");
            }
        Console.WriteLine((args.Contains("--measure-only")?"MEASURE":"PASS")+" 12 fixed-seed stationary/moving opponent pressure checks");return results.Count;
    }
    static Result Measure(string level,int seed,bool moving)
    {
        var arena=Fixture.Flat(spawns:[(3,6),(9,6)]);var f=new Fixture(arena,2,seed,level);
        f.Session.AiTestAddHuman("cpu-1",1);f.Session.AiTestPosition("cpu-1",9.5f,6.5f);f.Step(61);
        var human=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var soft=new SoftBlockGrid(arena.Width,arena.Height);var known=new HashSet<string>();
        int active=0,ready=0,bombs=0,pressure=0,first=-1,last=0,gap=0,deaths=0;
        bool alive=bot.Alive;
        for(int tick=0;tick<900;tick++)
        {
            // Keep the target alive solely to measure repeated engagement in one uninterrupted fight.
            // It still moves through ordinary input/physics; the bot gets no extra resources or information.
            human.InvincibleTimer=2;
            int leg=tick/40%4;
            human.Input=moving?new(leg==0?.35f:leg==2?-.35f:0,leg==1?.35f:leg==3?-.35f:0,false,false,false):default;
            if(f.Session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry){active++;if(bot.ActiveBombs<bot.BombCapacity)ready++;}
            f.Step();
            if(alive&&!bot.Alive)deaths++;alive=bot.Alive;
            foreach(var bomb in f.Session.AiTestBombs)if(bomb.OwnerId==bot.Id&&known.Add(bomb.Id))
            {
                bombs++;
                if(BlastGeometry.Contains(arena,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,human.X,human.Y,human.Z,bomb.IsRed))
                {pressure++;if(first<0)first=tick;gap=Math.Max(gap,tick-last);last=tick;}
            }
        }
        gap=Math.Max(gap,900-last);
        return new(level,seed,moving,active,ready,bombs,pressure,first,gap,deaths);
    }
}
