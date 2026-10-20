using System.Text.Json;
using Bomber.Game;

internal static class FrontPlacementChecks
{
    public static int Run()
    {
        Observe("sunken-temple",3);
        foreach(var stage in new[]{"rock-garden","up-and-down","sunken-temple"})
            foreach(var bots in new[]{3,7})if(stage!="sunken-temple"||bots!=3)Observe(stage,bots);
        return 6;
    }
    static void Observe(string stage,int bots)
    {
        var m=new LocalMatch(new MatchOptions {Stage=stage,HumanPlayers=1,BotPlayers=bots,Seed=64,Wins=1,RoundSeconds=60,SuddenDeath=false});
        var drops=new HashSet<string>();var blasts=new HashSet<string>();var placed=new HashSet<string>();int initial=0,minCover=int.MaxValue;
        for(int tick=0;tick<1900&&!m.IsEnded;tick++)
        {
            m.Tick();using var d=JsonDocument.Parse(m.StateJson!);var state=d.RootElement.GetProperty("d");
            int cover=Convert.FromBase64String(state.GetProperty("softBlocks").GetString()!).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
            if(tick==0)initial=cover;minCover=Math.Min(minCover,cover);
            foreach(var item in state.GetProperty("items").EnumerateArray())drops.Add(item.GetProperty("id").GetString()!);
            foreach(var e in state.GetProperty("events").EnumerateArray())
            {
                var kind=e.GetProperty("kind").GetString();if(kind=="bomb-drop")placed.Add(e.GetProperty("entityId").GetString()!);
                if(kind=="blast")blasts.Add(e.GetProperty("entityId").GetString()!);
            }
        }
        Console.WriteLine($"OBSERVE {stage} bots={bots} placed={placed.Count} blasts={blasts.Count} coverDestroyed={initial-minCover} drops={drops.Count}");
        if(placed.Count<2||blasts.Count<2)throw new Exception($"Bots failed repeated real front-placement combat on {stage}, bots={bots}.");
        if(initial<=minCover||drops.Count==0)throw new Exception($"Stock {stage}, bots={bots} no longer destroys cover and reveals real item drops within a one-minute seed64 match.");
        Console.WriteLine($"PASS {stage} bots={bots}: ordinary front placement destroys cover and reveals real items");
    }
}
