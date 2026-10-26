using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using static GameFixtures;

internal static class LedgeDropChecks
{
    public static int Run()
    {
        int count=0;
        var failures=new List<string>();
        void Check(string name,Action test)
        {
            try { test();count++;Console.WriteLine("PASS "+name); }
            catch(InvalidOperationException e) { failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+failures[^1]); }
        }
        Check("unlatched ghost drops an ordinary ledge",()=>
        {
            var cells=new CellDef[11,11];
            for(int x=0;x<11;x++)for(int z=0;z<11;z++)cells[x,z]=x>=4?CellDef.Floor2:CellDef.Floor0;
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,SuddenDeath=false},
                new StageLayout {Id="ghost-ledge",Width=11,Height=11,Cells=cells,Spawns=[(4,3),(8,7),(9,9)],SoftBlockDensity=0});
            Fight(match);
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,default);Step(match,90);
            Assert(Player(match).GetProperty("ghost").GetBoolean(),"Explosion did not create a ghost.");
            match.SetInput(0,new PlayerInput(-1,0));Step(match,8);
            Assert(Player(match).GetProperty("falling").GetBoolean(),"Ghost teleported to the lower floor instead of falling: "+Player(match));
            match.SetInput(0,default);Step(match,30);
            Assert(Player(match).GetProperty("ghost").GetBoolean()&&Player(match).GetProperty("y").GetSingle()==0,
                "Ghost did not survive and land on the lower floor: "+Player(match));
        });
        foreach(bool rotate in new[]{false,true})foreach(int direction in new[]{-1,1})
        Check($"covered ledge lands and exits: rotate={rotate}, direction={direction}",()=>
        {
            var match=CoveredFixture(rotate,direction);
            var input=rotate?new PlayerInput(0,direction):new PlayerInput(direction,0);
            match.SetInput(0,input);
            for(int tick=0;tick<25&&!Events(match).Any(e=>Kind(e)=="player-land");tick++)match.Tick();
            match.SetInput(0,default);Step(match,2);
            var p=Player(match);
            Assert(Events(match).Any(e=>Kind(e)=="player-fall"),"Cover blocked departure.");
            Assert(!p.GetProperty("airborne").GetBoolean()&&Math.Abs(p.GetProperty("y").GetSingle()-.61f)<.001f,
                "Player did not land on the crate's physical top: "+p);
            Assert(!p.GetProperty("stunned").GetBoolean(),"Crate landing added throw stun.");
            match.SetInput(0,input);Step(match,25);
            Assert(Player(match).GetProperty("y").GetSingle()==0&&!Player(match).GetProperty("airborne").GetBoolean(),
                "Walking off the crate did not restore lower-floor movement: "+Player(match));
            Assert(Events(match).Count(e=>Kind(e)=="player-fall")==2,"Crate exit snapped down instead of falling.");
        });
        foreach(float coverHeight in new[]{1.3f,.61f})
        Check($"cover intersecting the body still blocks: height={coverHeight}",()=>
        {
            var match=CoveredFixture(false,1,coverHeight,coverHeight<1?0:1);
            match.SetInput(0,new PlayerInput(1,0));Step(match,20);
            Assert(Player(match).GetProperty("x").GetSingle()<10-.39f,"Player entered a solid crate side.");
            Assert(!Events(match).Any(e=>Kind(e)=="player-fall"),"A tall crate was treated as a downhill exit.");
        });
        foreach(bool rotate in new[]{false,true})foreach(int direction in new[]{-1,1})
        Check($"ghost drops onto cover: rotate={rotate}, direction={direction}",()=>
        {
            var match=CoveredFixture(rotate,direction,upper:2);
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,default);Step(match,90);
            Assert(Player(match).GetProperty("ghost").GetBoolean(),"Fixture failed to create a ghost.");
            Assert(Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!).Any(b=>b!=0),"Ghost fixture lost its lower cover.");
            match.SetInput(0,rotate?new PlayerInput(0,direction):new PlayerInput(direction,0));Step(match,8);
            Assert(Player(match).GetProperty("falling").GetBoolean(),"Ghost could not depart above lower cover.");
            match.SetInput(0,default);Step(match,25);
            Assert(Player(match).GetProperty("ghost").GetBoolean()&&Math.Abs(Player(match).GetProperty("y").GetSingle()-.61f)<.001f,
                "Ghost did not land on the lower cover: "+Player(match));
        });
        Check("upper-deck cover does not block its lower passage",()=>
        {
            var stage=FlatArena([(4,3),(8,7),(9,9)],11);
            stage.Cells[5,3]=new CellDef {Kind=CellKind.Floor,Tier=3,Below=CellDef.Floor0};
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,SuddenDeath=false},
                new StageLayout {Id="deck-cover",Width=11,Height=11,Cells=stage.Cells,Spawns=stage.Spawns,
                    FixedSoftBlocks=new HashSet<(int,int)>{(5,3)}});
            Fight(match);match.SetInput(0,new PlayerInput(1,0));Step(match,12);
            Assert(Player(match).GetProperty("x").GetSingle()>6&&Player(match).GetProperty("y").GetSingle()==0,
                "Cover above the deck blocked the lower story: "+Player(match));
        });
        foreach(bool covered in new[]{false,true})
        Check($"Pyramid lower ledge drops with covered={covered}",()=>
        {
            var terrain=BlastStages.Get(BlastStages.SunkenTemple);
            var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,SuddenDeath=false},
                new StageLayout {Id="pyramid-covered-ledge",Width=terrain.Width,Height=terrain.Height,Cells=terrain.Cells,
                    Spawns=[(4,16),(12,12),(20,6)],FixedSoftBlocks=covered?new HashSet<(int,int)>{(5,16)}:new HashSet<(int,int)>()});
            Fight(match);
            Assert(State(match).GetProperty("softBlocks").GetString() is not null,"Fixture did not publish cover.");
            match.SetInput(0,new PlayerInput(1,0));Step(match,8);
            Assert(Events(match).Any(e=>Kind(e)=="player-fall"),"Lower-court block prevented ledge departure: "+Player(match));
            match.SetInput(0,default);Step(match,20);
            float expected=covered?terrain.SoftBlockHeight:0;
            Assert(Math.Abs(Player(match).GetProperty("y").GetSingle()-expected)<.001f,"Pyramid drop landed inside cover or above its floor.");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join(Environment.NewLine,failures));
        Console.WriteLine($"Passed {count} ledge drop integration checks.");return count;
    }

    private static LocalMatch CoveredFixture(bool rotate,int direction,float coverHeight=.61f,int upper=1)
    {
        var cells=new CellDef[20,20];
        for(int x=0;x<20;x++)for(int z=0;z<20;z++)
            cells[x,z]=(direction<0?(rotate?z:x)>=10:(rotate?z:x)<10)?new CellDef {Kind=CellKind.Floor,Tier=upper}:CellDef.Floor0;
        int start=direction<0?10:9,block=direction<0?9:10;
        var spawn=rotate?(4,start):(start,4);
        var cover=rotate?(4,block):(block,4);
        var match=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,SuddenDeath=false},
            new StageLayout {Id="covered-ledge",Width=20,Height=20,Cells=cells,Spawns=[spawn,(16,16),(18,18)],
                FixedSoftBlocks=new HashSet<(int,int)>{cover},SoftBlockHeight=coverHeight});
        Fight(match);return match;
    }
}
