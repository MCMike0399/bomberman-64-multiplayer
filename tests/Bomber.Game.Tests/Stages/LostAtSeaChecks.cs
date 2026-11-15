using System.Numerics;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

internal static class LostAtSeaChecks
{
    public static void Run(Action<string,Action> run)
    {
        run("Lost at Sea keeps four diamond towers and eight supported nonslip rim starts",()=>
        {
            var stage=BlastStages.Get(BlastStages.LostAtSea);
            var towers=new HashSet<(int,int)>{(10,6),(6,10),(14,10),(10,14)};
            for(int x=1;x<20;x++)for(int z=1;z<20;z++)
                Assert(stage.At(x,z).Kind==(towers.Contains((x,z))?CellKind.Pillar:CellKind.Floor),"Lost at Sea topology differs from its glass court.");
            Assert(stage.Spawns.Distinct().Count()==8&&stage.Spawns.All(s=>stage.At(s.X,s.Z).IsPassable&&!SlipperyGlassRules.OnGlass(stage,s.X+.5f,s.Z+.5f)),"Every seat needs a distinct grippy rim start.");
            Assert(SlipperyGlassRules.OnGlass(stage,10.5f,10.5f)&&!SlipperyGlassRules.OnGlass(stage,2.5f,10.5f),"Glass/rim traction boundary is incorrect.");
        });
        run("Lost at Sea release coasts and reversing direction takes anticipation",()=>
        {
            var release=Practice();Walk(release,2.5f,8.5f);release.SetInput(0,new(1,0));Step(release,40);
            float start=Player(release).GetProperty("x").GetSingle();release.SetInput(0,default);Step(release,10);
            Assert(Player(release).GetProperty("x").GetSingle()>start+.8f,"Releasing the stick stopped glass momentum immediately.");
            var reverse=Practice();Walk(reverse,2.5f,8.5f);reverse.SetInput(0,new(1,0));Step(reverse,40);
            start=Player(reverse).GetProperty("x").GetSingle();reverse.SetInput(0,new(-1,0));Step(reverse,10);
            Assert(Player(reverse).GetProperty("x").GetSingle()>start+.5f,"An instantaneous reversal bypassed slippery glass.");
            Step(reverse,25);
            Assert(Player(reverse).GetProperty("x").GetSingle()<start+1,"Held countersteering failed to reverse actual travel.");
        });
        run("Lost at Sea pale rim stops immediately and ordinary arenas keep their movement",()=>
        {
            foreach(string id in new[]{BlastStages.LostAtSea,BlastStages.RockGarden})
            {
                var match=new LocalMatch(new MatchOptions{Stage=id,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(match);
                match.SetInput(0,new(0,1));Step(match,12);match.SetInput(0,default);Step(match,2);
                var before=Player(match);Step(match,15);var after=Player(match);
                Assert(before.GetProperty("x").GetSingle()==after.GetProperty("x").GetSingle()&&before.GetProperty("z").GetSingle()==after.GetProperty("z").GetSingle(),"Nonslip movement gained coast on "+id);
            }
        });
        run("Lost at Sea planted bomb aim stops carried momentum without changing the floor",()=>
        {
            var match=Practice();Walk(match,2.5f,8.5f);match.SetInput(0,new(1,0));Step(match,35);
            match.SetInput(0,new(1,0,A:true,B:true));Step(match,2);match.SetInput(0,new(0,0,B:true,Aim:true));Step(match,4);
            var before=Player(match);Assert(before.GetProperty("aiming").GetBoolean(),"Ordinary carry and aim failed on glass.");Step(match,10);
            Assert(before.GetProperty("x").GetSingle()==Player(match).GetProperty("x").GetSingle()&&Player(match).GetProperty("y").GetSingle()==0,"Planted aim leaked slide velocity or invented a floor height.");
        });
        run("Lost at Sea neutral friction is bounded and cleanup clears inertial state",()=>
        {
            var velocity=new Vector2(4.5f,0);
            for(int i=0;i<60;i++)velocity=SlipperyGlassRules.Advance(velocity,Vector2.Zero,1f/30);
            Assert(velocity==Vector2.Zero,"Neutral glass motion never settled.");
            var player=new BlastPlayer{SlideVX=4,SlideVZ=-2};player.ClearInput();
            Assert(player.SlideVX==0&&player.SlideVZ==0,"Round/disconnect/practice input reset retained old glass motion.");
        });
        run("Lost at Sea kicked bombs coast on glass and brake on the nonslip rim",()=>
        {
            foreach(bool glass in new[]{false,true})
            {
                var match=Practice();Walk(match,2.5f,8.5f);
                match.SetInput(0,glass?new(1,0):new(0,1));Step(match,1);match.SetInput(0,default);Step(match,1);
                match.SetInput(0,new(0,0,A:true));Step(match,2);match.SetInput(0,default);Step(match,2);
                match.SetInput(0,glass?new(1,0,A:true):new(0,1,A:true));Step(match,2);match.SetInput(0,default);Step(match,30);
                var bomb=State(match).GetProperty("bombs")[0];
                Assert(bomb.GetProperty("fuse").GetSingle()<2,"Glass changed a real kicked bomb fuse.");
                Assert(bomb.GetProperty("state").GetInt32()==(glass?1:0),"Kicked bomb did not distinguish slippery glass from grippy rim.");
                if(glass)Assert(bomb.GetProperty("x").GetSingle()>10,"Glass kick lost its sustained momentum.");
            }
        });
        run("Lost at Sea cargo is seeded, scalable and absent from the safe rim",()=>
        {
            var patterns=new HashSet<string>();
            for(int seed=0;seed<16;seed++)foreach(int percent in new[]{0,50,100})
            {
                var match=new LocalMatch(new MatchOptions{Stage=BlastStages.LostAtSea,HumanPlayers=8,BotPlayers=0,Seed=seed,Advanced=new(){BlockPercent=percent}});
                string encoded=State(match).GetProperty("softBlocks").GetString()!;var bits=Convert.FromBase64String(encoded);
                Assert(bits.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==32*percent/100,"Cargo did not honor block density.");
                if(percent==100)patterns.Add(encoded);
                for(int x=0;x<21;x++)for(int z=0;z<21;z++)if((bits[(z*21+x)/8]&(1<<((z*21+x)%8)))!=0)
                    Assert(x is >=5 and <=15&&z is >=5 and <=15&&x!=10&&z!=10,"Cargo blocked the rim or a protected cross lane.");
            }
            Assert(patterns.Count>=12,"Cargo is still stamping a fixed layout.");
        });
        run("Lost at Sea Hurry uses fireballs and leaves the silver rim in place",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.LostAtSea,HumanPlayers=3,BotPlayers=0,RoundSeconds=60,Ghosts=false});Step(match,64);
            Assert(State(match).GetProperty("hurryMode").GetString()=="meteors"&&State(match).GetProperty("closedLeft").GetInt32()==0,"Slippery glass still closes columns.");
            Assert(State(match).GetProperty("meteors").GetArrayLength()>0,"Hurry never published a fireball warning.");Step(match,38);
            Assert(!Player(match).GetProperty("alive").GetBoolean(),"Stationary rim player survived the published meteor target.");
        });
    }

    private static LocalMatch Practice()
    {
        var match=new LocalMatch(new MatchOptions{Stage=BlastStages.LostAtSea,HumanPlayers=1,BotPlayers=0,Practice=true});Fight(match);return match;
    }
}
