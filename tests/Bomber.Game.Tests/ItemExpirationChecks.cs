using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class ItemExpirationChecks
{
    public static int Run()
    {
        foreach(bool practice in new[]{false,true})
        {
            var cells=new CellDef[15,15];
            for(int x=0;x<15;x++)for(int z=0;z<15;z++)cells[x,z]=CellDef.Floor0;
            var match=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=practice,SuddenDeath=false},
                new StageLayout{Id="expiration",Width=15,Height=15,Cells=cells,Spawns=[(2,2),(12,12)],SoftBlockDensity=0,InitialItems=[(7,7,"fire")]});
            int Count(){using var d=JsonDocument.Parse(match.StateJson!);return d.RootElement.GetProperty("d").GetProperty("items").GetArrayLength();}
            for(int i=0;i<64;i++)match.Tick();
            if(Count()!=1)throw new Exception("Uncollected item was missing before its lifetime.");
            for(int i=0;i<610;i++)match.Tick();
            if(Count()!=0)throw new Exception($"Uncollected sandbox item never expired (practice={practice}).");
            Console.WriteLine($"PASS uncollected sandbox item expires (practice={practice})");
        }
        return 2;
    }
}
