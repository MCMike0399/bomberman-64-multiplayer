using System.Numerics;
using System.Reflection;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

internal static class BlizzardBattleChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(string name,Action test){test();checks++;Console.WriteLine("PASS Blizzard Battle "+name);}
        var stage=BlastStages.Get("blizzard-battle");
        Check("topology and eight supported starts",()=>
        {
            Assert(stage.Id=="blizzard-battle"&&stage.Width==19&&stage.Height==19,"Blizzard Battle must be its own enclosed square snowfield.");
            for(int x=0;x<19;x++)for(int z=0;z<19;z++)
                Assert(stage.At(x,z).Kind==(x is 0 or 18||z is 0 or 18?CellKind.Pillar:CellKind.Floor)&&stage.At(x,z).Tier==0,"Snowfield must be flat, with no interior permanent obstacles.");
            Assert(stage.Spawns.Distinct().Count()==8&&stage.Spawns.All(s=>stage.At(s.X,s.Z).IsPassable),"Eight distinct snowfield starts required.");
        });
        Check("sixteen authored cones, block settings and spawn safety",()=>
        {
            foreach(int percent in new[]{0,50,100})
            {
                var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=8,BotPlayers=0,Advanced=new AdvancedRules{BlockPercent=percent}});
                var bits=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                Assert(bits.Sum(b=>BitOperations.PopCount((uint)b))==16*percent/100,"Ice cone count ignored advanced settings.");
                for(int z=0;z<19;z++)for(int x=0;x<19;x++)if((bits[(z*19+x)/8]&(1<<((z*19+x)%8)))!=0)
                {
                    Assert(!stage.ClearCells.Contains((x,z)),"Cone outside reference composition.");
                    Assert(stage.Spawns.All(s=>Math.Abs(s.X-x)+Math.Abs(s.Z-z)>1),"Cone blocks spawn escape.");
                }
            }
        });
        Check("seeded four-diagonal gusts, calm and bounded ramps",()=>
        {
            var directions=new HashSet<(int,int)>();bool calm=false;
            for(int tick=0;tick<3600;tick++)
            {
                var wind=BlizzardRules.Wind(tick/30f,64,1);
                Assert(wind.Length()<=BlizzardRules.WindSpeed+.0001f,"Wind exceeds movement budget.");
                if(wind.Length()>.1f)directions.Add((Math.Sign(wind.X),Math.Sign(wind.Y)));else calm=true;
                Assert(wind==BlizzardRules.Wind(tick/30f,64,1),"Weather is not deterministic.");
            }
            Assert(calm&&directions.Count==4,"Weather must include all four diagonals and calm.");
        });
        Check("real input countersteers and walls contain wind",()=>
        {
            var idle=Match();Fight(idle);float x=Player(idle).GetProperty("x").GetSingle();Step(idle,60);
            Assert(Player(idle).GetProperty("x").GetSingle()>x+.7f,"Wind did not move an idle living player.");
            var active=Match();Fight(active);active.SetInput(0,new PlayerInput(-1,0));Step(active,60);
            Assert(Player(active).GetProperty("x").GetSingle()<x,"Ordinary countersteering cannot overcome wind.");
            Step(active,600);var p=Player(active);
            Assert(p.GetProperty("alive").GetBoolean()&&p.GetProperty("x").GetSingle()>=1.39f&&p.GetProperty("z").GetSingle()>=1.39f,"Wind/input penetrated enclosure.");
        });
        Check("resting bomb drift preserves fuse and carried attachment",()=>
        {
            var match=Match();Fight(match);match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,new PlayerInput(0,-1));
            var placed=State(match).GetProperty("bombs")[0];float x=placed.GetProperty("x").GetSingle();float fuse=placed.GetProperty("fuse").GetSingle();
            Step(match,30);var moved=State(match).GetProperty("bombs")[0];
            Assert(moved.GetProperty("x").GetSingle()>x+.5f&&Math.Abs(moved.GetProperty("fuse").GetSingle()-(fuse-1))<.04f,"Drift stopped a fuse or failed to move a free bomb.");
            var carried=Match();Fight(carried);carried.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(carried,2);carried.SetInput(0,new PlayerInput(0,0,B:true));Step(carried,30);
            var bomb=State(carried).GetProperty("bombs")[0];var p=Player(carried);
            Assert(bomb.GetProperty("state").GetInt32()==(int)BombState.Carried&&Math.Abs(bomb.GetProperty("x").GetSingle()-p.GetProperty("x").GetSingle())<.02f,"Carried bomb drifted independently of carrier.");
        });
        Check("wind-aware static projection matches real aimed flight",()=>
        {
            var match=Match();Fight(match);
            match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(match,2);
            match.SetInput(0,default);Step(match,2);
            var session=(BlastSession)typeof(LocalMatch).GetField("_session",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(match)!;
            var bomb=session.AiTestBombs.Single();Assert(bomb.State==BombState.Flying,"Ordinary aimed release did not fly.");
            var weather=State(match).GetProperty("blizzard");float clock=weather.GetProperty("time").GetSingle();
            var projection=BombThrowRules.Project(new(bomb.X,bomb.FlyY,bomb.Z),new(bomb.FlyVX,bomb.FlyVY,bomb.FlyVZ),
                stage.SurfaceAt,_=>false,cellKind:(x,z)=>stage.At(x,z).Kind,hasCover:(_,_)=>false,stage:stage,
                windAt:t=>BlizzardRules.Wind(clock+t+1f/30,64,1));
            for(int i=0;i<90&&bomb.State==BombState.Flying;i++)match.Tick();
            Assert(Vector2.Distance(new(bomb.X,bomb.Z),new(projection.Last.X,projection.Last.Z))<.08f,"Authoritative airborne drift diverged from advisory landing.");
        });
        Check("bilateral avalanche eliminates side seats and resets next round",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=8,BotPlayers=0,RoundSeconds=60,Ghosts=false,Advanced=new AdvancedRules{BlockPercent=0}});
            Step(match,70);Assert(State(match).GetProperty("hurryMode").GetString()=="avalanche","Wrong sudden-death hazard.");
            Step(match,280);var s=State(match);
            Assert(s.GetProperty("closedLeft").GetInt32()==3&&s.GetProperty("closedRight").GetInt32()==3,"Avalanches did not advance equally at the authored interval.");
            Assert(s.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean()),"Avalanches failed to eliminate swept players.");
            for(int i=0;i<1800&&State(match).GetProperty("round").GetInt32()==1;i++)match.Tick();
            Assert(State(match).GetProperty("round").GetInt32()==2&&State(match).GetProperty("closedLeft").GetInt32()==0,"New round retained packed avalanche columns.");
        });
        Check("practice and disabled sudden death keep avalanches off",()=>
        {
            foreach(bool practice in new[]{true,false})
            {
                var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=2,BotPlayers=0,RoundSeconds=60,Practice=practice,SuddenDeath=false});Step(match,400);
                Assert(State(match).GetProperty("closedLeft").GetInt32()==0&&State(match).GetProperty("hurryMode").GetString()=="none","Disabled hazard closed the snowfield.");
            }
            var garden=new LocalMatch(new MatchOptions{Stage="rock-garden",HumanPlayers=2,BotPlayers=0});Step(garden,100);
            Assert(State(garden).GetProperty("blizzard").ValueKind==System.Text.Json.JsonValueKind.Null,"Blizzard leaked into another stage.");
        });
        Console.WriteLine($"Passed {checks} Blizzard Battle integration checks.");return checks;
    }
    private static LocalMatch Match()=>new(new MatchOptions{Stage="blizzard-battle",HumanPlayers=2,BotPlayers=0,Practice=true,Seed=64,Advanced=new AdvancedRules{BlockPercent=0}});
}
