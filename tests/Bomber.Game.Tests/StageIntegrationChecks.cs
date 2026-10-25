using System.Reflection;
using Bomber.Game;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using static GameFixtures;

internal static class StageIntegrationChecks
{
    public static int Run()
    {
        var castle=BlastStages.Get(BlastStages.TopRules);
        var origin=new System.Numerics.Vector3(5.01f,BombThrowRules.LaunchHeight,4.5f);
        var flight=BombThrowRules.AdvanceStatic(origin,BombThrowRules.Velocity(-1,0,1,3),.05f,
            (x,z)=>castle.SurfaceAt(x,z,origin.Y),(x,z)=>castle.AtHeight(x,z,origin.Y).Kind,(_,_)=>false,gravityScale:1.6f,stage:castle);
        Assert(!castle.DeckOverlaps((int)flight.Position.X,(int)flight.Position.Z,flight.Position.Y,BombThrowRules.CoverHeight),"fast charged throw rose through the swept bridge underside");
        Console.WriteLine("PASS fast charged throw resolves swept roof contact");
        var garden=BlastStages.Get(BlastStages.RockGarden);var graph=NavigationGraph.For(garden);var danger=new TimedDangerField(graph,garden);var soft=new SoftBlockGrid(garden.Width,garden.Height);
        var bomb=new BlastBomb {Id="chain",X=6.5f,Z=6.5f,CellX=6,CellZ=6,Tier=0,FireLevel=5,FuseTimer=2.5f};
        danger.Build(soft,[bomb],[],[new BlastMeteor{Id="warning",X=6.5f,Z=6.5f,Remaining=.4f}]);
        Assert(!danger.Safe(graph.Cell(10.5f,6.5f),.4f,.8f),"meteor-triggered bomb chain was omitted outside the impact circle");
        Console.WriteLine("PASS meteor danger advances nearby bomb chain time");
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
        Console.WriteLine("PASS held-player clearance preserves lower support beneath the ring");return 3;
    }
}
