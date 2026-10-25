using System.Reflection;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

internal static class StageIntegrationChecks
{
    public static int Run()
    {
        var original=BlastStages.Get(BlastStages.UpAndDown);
        var layout=new StageLayout {Id=original.Id,Width=original.Width,Height=original.Height,Cells=original.Cells,
            Spawns=[(18,20),(17,20)],RisingWater=true,SoftBlockDensity=0};
        var match=new LocalMatch(new MatchOptions {Stage=original.Id,HumanPlayers=2,BotPlayers=0,Practice=true},layout);
        Fight(match);
        // Explicit fixture condition: a downed partner. All subsequent movement, pickup and release are real inputs.
        var session=(BlastSession)typeof(LocalMatch).GetField("_session",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(match)!;
        var victim=session.AiTestPlayers.Single(p=>p.Slot==1);victim.StunTimer=victim.RecoveryTotal=4;
        match.SetInput(0,new(-1,0));Step(match,1);match.SetInput(0,default);Step(match,1);
        match.SetInput(0,new(0,0,B:true));Step(match,2);
        Assert(Player(match).GetProperty("carrying").GetString()=="player","fixture did not reach a real player hold");
        match.SetInput(0,new(0,1,B:true));
        for(int i=0;i<12;i++)
        {
            Step(match,2);var held=Player(match,1);
            float x=held.GetProperty("x").GetSingle(),z=held.GetProperty("z").GetSingle(),y=held.GetProperty("y").GetSingle();
            var cell=layout.At((int)x,(int)z);
            Assert(cell.Below is null || y+1.2f<=cell.Tier-CellDef.DeckThickness+.001f,"held player's body penetrated the ring underside");
        }
        match.SetInput(0,new(-1,0,B:true));Step(match,1);
        match.SetInput(0,default);Step(match,40);
        Assert(Player(match,1).GetProperty("y").GetSingle()<3.8f,"released partner teleported onto the upper ring");
        Console.WriteLine("PASS held-player clearance preserves lower support beneath the ring");return 1;
    }
}
