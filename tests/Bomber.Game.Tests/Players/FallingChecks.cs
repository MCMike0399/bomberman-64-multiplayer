using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using static GameFixtures;

internal static class FallingChecks
{
    private sealed record FallSample(int Height, string Input, int Ticks, float Travel, float Sideways);

    public static int Run()
    {
        int count=0;
        foreach(int height in new[]{1,2,4})
        {
            var fall=Drop(height,new PlayerInput(-1,0),"forward");
            Console.WriteLine(JsonSerializer.Serialize(fall));
            Assert(fall.Ticks<=new[]{0,10,14,0,19}[height],"Ledge fall hangs in the air too long: "+fall);
            Assert(fall.Travel<=new[]{0,.8f,1.1f,0,1.5f}[height],"Held movement carries the falling player too far: "+fall);
            count++;
        }
        var side=Drop(4,new PlayerInput(0,1),"sideways");
        Assert(side.Sideways>.1f&&side.Sideways<1.2f,"Air correction should remain useful but limited: "+side);
        count++;
        var release=Drop(4,default,"release");
        Assert(release.Travel>.12f&&release.Travel<.45f,"Releasing the stick must settle a short coast instead of stopping instantly or gliding: "+release);
        count++;
        foreach(string effect in new[]{"none","haste","slow","tiny","big"})
        {
            var fall=Drop(2,new PlayerInput(-1,0),effect,effect:effect);
            Assert(fall.Ticks<=14&&fall.Travel<1.5f,"Status movement restored a floaty fall: "+fall);
            count++;
        }
        foreach(bool rotate in new[]{false,true})foreach(int direction in new[]{-1,1})
        {
            var fall=Drop(2,rotate?new PlayerInput(0,direction):new PlayerInput(direction,0),"cardinal",rotate:rotate,direction:direction);
            Assert(fall.Travel>.3f&&fall.Travel<1.1f,"Ledge fall differs by direction: "+fall);
            count++;
        }
        var carrying=Drop(2,new PlayerInput(-1,0,B:true),"carrying",carry:true);
        Assert(carrying.Travel<1.1f,"Carrying bypassed ledge air limits: "+carrying);
        count++;
        var reversing=Fixture(4);
        BeginFall(reversing,new PlayerInput(-1,0));
        Step(reversing,4);
        float before=Player(reversing).GetProperty("x").GetSingle();
        reversing.SetInput(0,new PlayerInput(1,0));Step(reversing,2);
        Assert(Player(reversing).GetProperty("x").GetSingle()<before-.02f,"Air reversal instantly erased outward momentum.");
        count++;
        Console.WriteLine($"Passed {count} falling integration checks.");
        return count;
    }

    public static void Measure()
    {
        foreach(int height in new[]{1,2,4})
            foreach(var input in new[]{("forward",new PlayerInput(-1,0)),("release",default(PlayerInput)),("reverse",new PlayerInput(1,0)),("sideways",new PlayerInput(0,1))})
                Console.WriteLine(JsonSerializer.Serialize(Drop(height,input.Item2,input.Item1)));
    }

    private static LocalMatch Fixture(int height,string effect="none",bool rotate=false,int direction=-1)
    {
        var cells=new CellDef[32,20];
        for(int x=0;x<32;x++)for(int z=0;z<20;z++)
            cells[x,z]=(direction<0?(rotate?z:x)>=(rotate?10:16):(rotate?z:x)<(rotate?10:16))?new CellDef{Kind=CellKind.Floor,Tier=height}:CellDef.Floor0;
        int start=(rotate?10:16)-(direction>0?1:0);
        var spawn=rotate?(4,start):(start,4);
        var match=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0,SuddenDeath=false},
            new StageLayout{Id="falling-fixture",Width=32,Height=20,Cells=cells,Spawns=[spawn,(28,15),(30,18)],SoftBlockDensity=0,
                InitialItems=effect=="none"?[]:[(spawn.Item1,spawn.Item2,effect)]});
        Fight(match);
        Step(match,2);
        Assert(Player(match).GetProperty("statusEffect").GetString()==effect,"Fixture did not collect its status pickup.");
        return match;
    }

    private static void BeginFall(LocalMatch match,PlayerInput input)
    {
        match.SetInput(0,input);
        int departure=0;
        while(!Player(match).GetProperty("falling").GetBoolean()&&departure++<12)match.Tick();
        Assert(Player(match).GetProperty("falling").GetBoolean(),"Ordinary movement did not step off the fixture ledge.");
    }

    private static FallSample Drop(int height,PlayerInput airInput,string name,string effect="none",bool rotate=false,int direction=-1,bool carry=false)
    {
        var match=Fixture(height,effect,rotate,direction);
        if(carry)
        {
            match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,2);
            Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Fixture failed ordinary carry-held bomb creation.");
        }
        BeginFall(match,rotate?new PlayerInput(0,direction,B:carry):new PlayerInput(direction,0,B:carry));
        // Events retain the actual lip position even when snapshots arrive every other tick.
        var start=Events(match).Last(e=>Kind(e)=="player-fall");
        match.SetInput(0,airInput);
        int ticks=0;
        while(Player(match).GetProperty("airborne").GetBoolean()&&ticks++<60)match.Tick();
        var land=Events(match).LastOrDefault(e=>Kind(e)=="player-land");
        Assert(land.ValueKind==JsonValueKind.Object,"Fall failed to land within two seconds.");
        Assert(!Player(match).GetProperty("stunned").GetBoolean(),"Ordinary ledge landing added throw recovery stun.");
        Assert(land.GetProperty("y").GetSingle()==0&&land.GetProperty("reason").GetString()=="fall","Ledge fall landed on the wrong surface or used throw recovery.");
        float landed=Player(match).GetProperty(rotate?"z":"x").GetSingle();
        match.SetInput(0,rotate?new PlayerInput(0,direction,B:carry):new PlayerInput(direction,0,B:carry));Step(match,4);
        Assert((Player(match).GetProperty(rotate?"z":"x").GetSingle()-landed)*direction>.35f,"Landing did not immediately restore ground movement.");
        if(carry)Assert(Player(match).GetProperty("carrying").GetString()=="bomb","Fall detached the carried bomb.");
        return new(height,name,land.GetProperty("tick").GetInt32()-start.GetProperty("tick").GetInt32()+1,
            (land.GetProperty(rotate?"z":"x").GetSingle()-start.GetProperty(rotate?"z":"x").GetSingle())*direction,
            land.GetProperty(rotate?"x":"z").GetSingle()-start.GetProperty(rotate?"x":"z").GetSingle());
    }
}
