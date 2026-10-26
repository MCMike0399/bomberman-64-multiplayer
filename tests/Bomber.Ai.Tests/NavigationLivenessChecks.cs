using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

static class NavigationLivenessChecks
{
    public static int Run()
    {
        // A visible idle opponent stands between the CPU and a desirable pickup.
        // Lock only the opponent's input; the CPU still uses the real tick and collision pipeline.
        var stage=Fixture.Flat(spawns:[(3,5),(5,5)]);stage.Cells[6,5]=CellDef.Pillar;
        var f=new Fixture(stage,2);
        f.Step(62);
        var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var blocker=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        f.Session.AiTestPosition(bot.Id,3.5f,5.5f);
        f.Session.AiTestPosition(blocker.Id,5.5f,5.5f);
        bot.BombCapacity=0;blocker.BombCapacity=0;blocker.BotThinkTimer=10000;blocker.Input=default;
        f.Session.AiTestAddItem(5,8,"heart");
        f.Step(180);
        Console.WriteLine($"body route: bot=({bot.X:F3},{bot.Z:F3}) blocker=({blocker.X:F3},{blocker.Z:F3}) heart={bot.HasHeart}");
        if(!bot.HasHeart)throw new Exception("CPU must route around a body to reach a visible pickup within six seconds");
        Console.WriteLine("PASS CPU reroutes around dynamic bodies without bypassing collisions");

        var gap=Fixture.Flat(spawns:[(3,4),(10,10)]);
        for(int z=1;z<gap.Height-1;z++)if(z!=5)gap.Cells[6,z]=CellDef.Pillar;
        var narrow=new Fixture(gap,2);narrow.Step(62);
        var walker=narrow.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var spectator=narrow.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        walker.BombCapacity=0;spectator.BombCapacity=0;spectator.BotThinkTimer=10000;spectator.Input=default;
        narrow.Session.AiTestPosition(walker.Id,3.82f,4.92f);
        narrow.Session.AiTestAddItem(8,6,"heart");narrow.Step(180);
        if(!walker.HasHeart||!walker.Alive)throw new Exception("Off-center CPU must center and traverse a one-cell gap");
        Console.WriteLine("PASS off-center CPU centers and traverses a one-cell gap");

        var corridor=Fixture.Flat(spawns:[(3,5),(5,5)]);
        for(int x=1;x<corridor.Width-1;x++)for(int z=1;z<corridor.Height-1;z++)
            if(!(z==5&&x<=5)&&!(x==5&&z>=4))corridor.Cells[x,z]=CellDef.Pillar;
        var changing=new Fixture(corridor,2);changing.Step(62);
        var waiting=changing.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var opposing=changing.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        changing.Session.AiTestPosition(waiting.Id,3.5f,5.5f);changing.Session.AiTestPosition(opposing.Id,5.5f,5.5f);
        waiting.BombCapacity=0;opposing.BombCapacity=0;opposing.BotThinkTimer=10000;opposing.StunTimer=10000;opposing.Input=default;
        changing.Session.AiTestAddItem(5,8,"heart");changing.Step(90);
        if(waiting.X>opposing.X||MathF.Abs(waiting.X-opposing.X)<.79f)throw new Exception("CPU must respect collision while a narrow corridor is blocked");
        changing.Session.AiTestPosition(opposing.Id,5.5f,4.5f);opposing.Input=default;
        changing.Step(180);
        Console.WriteLine($"reopened: bot=({waiting.X:F3},{waiting.Z:F3}) blocker=({opposing.X:F3},{opposing.Z:F3}) alive={waiting.Alive} input={waiting.Input}");
        if(!waiting.HasHeart)throw new Exception("CPU must resume routing after a dynamic corridor blocker leaves");
        Console.WriteLine("PASS blocked CPU preserves body collision and resumes when a corridor reopens");
        var threatened=new Fixture(stage,2);threatened.Step(62);
        var evader=threatened.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var obstacle=threatened.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        threatened.Session.AiTestPosition(evader.Id,3.5f,5.5f);threatened.Session.AiTestPosition(obstacle.Id,5.5f,5.5f);
        evader.BombCapacity=0;obstacle.BombCapacity=0;obstacle.BotThinkTimer=10000;obstacle.Input=default;
        threatened.Session.AiTestAddItem(5,8,"heart");
        threatened.Session.AiTestAddBomb(new BlastBomb {Id="route-danger",OwnerId="visible-enemy",X=4.5f,Z=4.5f,CellX=4,CellZ=4,FuseTimer=.9f});
        threatened.Step(180);
        if(!evader.Alive||!evader.HasHeart)throw new Exception("CPU rerouting around a blocked body must preserve timed blast safety");
        Console.WriteLine("PASS body detour respects a visible timed blast and resumes its pickup route");
        var diagonal=new Fixture(Fixture.Flat(spawns:[(4,5),(6,6)]),2);diagonal.Step(62);
        var cornered=diagonal.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var centeredBody=diagonal.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        diagonal.Session.AiTestPosition(cornered.Id,4.034f,5.034f);
        diagonal.Session.AiTestPosition(centeredBody.Id,4.6f,5.6f);
        cornered.BombCapacity=0;centeredBody.BombCapacity=0;centeredBody.StunTimer=10000;centeredBody.BotThinkTimer=10000;centeredBody.Input=default;
        diagonal.Session.AiTestAddItem(8,8,"heart");diagonal.Step(180);
        if(!cornered.HasHeart)throw new Exception("Diagonal body contact must not permanently prevent a CPU centering before a turn");
        Console.WriteLine("PASS diagonal body contact resolves through ordinary lateral movement before centering");
        return 5;
    }
}
