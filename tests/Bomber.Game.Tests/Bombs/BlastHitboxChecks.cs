using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

internal static class BlastHitboxChecks
{
    public static int Run()
    {
        int count=0;
        foreach(int pumps in new[]{0,3})
        foreach(bool heart in new[]{false,true})
        foreach(bool expired in new[]{false,true})
        {CheckLateEntry(pumps,heart,expired);count++;}
        CheckLateEntry(3,false,false,4);count++;
        foreach(bool red in new[]{false,true}){CheckCoverDuringBloom(red);count++;}
        foreach(int pumps in new[]{3,0})
        foreach(bool practice in new[]{false,true})
        foreach(bool heart in new[]{false,true})
        {
            var cells=FlatArena([(3,3)],15).Cells;
            cells[3,3]=new CellDef{Kind=CellKind.Floor,Tier=4};
            var stage=new StageLayout{Id="fixture",Width=15,Height=15,Cells=cells,
                Spawns=[(3,3),(3,8),(12,12)],SoftBlockDensity=0,
                InitialItems=heart?[(3,3,"remote"),(3,8,"heart")]:[(3,3,"remote")]};
            var match=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0,Ghosts=false,SuddenDeath=false,Practice=practice},stage);
            Fight(match);
            match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(match,2);
            for(int i=0;i<pumps;i++)
            {
                match.SetInput(0,new PlayerInput(0,0,A:true,B:true,Aim:true));Step(match,2);
                match.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(match,15);
            }
            Assert(Player(match).GetProperty("remote").GetBoolean(),"Thrower did not collect the remote.");
            match.SetInput(0,default);
            bool detonated=false;
            for(int tick=0;tick<100;tick++)
            {
                match.Tick();
                var bomb=State(match).GetProperty("bombs")[0];
                var victim=Player(match,1);
                float x=bomb.GetProperty("x").GetSingle(),y=bomb.GetProperty("y").GetSingle(),z=bomb.GetProperty("z").GetSingle();
                float px=victim.GetProperty("x").GetSingle(),py=victim.GetProperty("y").GetSingle(),pz=victim.GetProperty("z").GetSingle();
                float radius=1.3f+pumps*.9f,dx=x-px,dz=z-pz;
                float nearestY=Math.Clamp(y,py,py+BlastGeometry.PlayerHeight);
                if(dx*dx+dz*dz+(y-py)*(y-py)<=radius*radius ||
                    dx*dx+dz*dz+(y-nearestY)*(y-nearestY)>radius*radius)continue;
                Assert(bomb.GetProperty("pumps").GetInt32()==pumps,"Throw did not complete its requested pumps.");
                Console.WriteLine($"WITNESS pumps={pumps} bomb=({x},{y},{z}) radius={radius} victim=({px},{py},{pz})");
                match.SetInput(0,new PlayerInput(0,0,R:true));Step(match,2);
                Assert(State(match).GetProperty("bombs").GetArrayLength()==0,"Remote did not detonate.");
                var blast=Events(match).Single(e=>Kind(e)=="blast");
                dx=blast.GetProperty("x").GetSingle()-px;dz=blast.GetProperty("z").GetSingle()-pz;y=blast.GetProperty("y").GetSingle();
                nearestY=Math.Clamp(y,py,py+BlastGeometry.PlayerHeight);
                Assert(dx*dx+dz*dz+(y-py)*(y-py)>radius*radius&&
                    dx*dx+dz*dz+(y-nearestY)*(y-nearestY)<=radius*radius,
                    "Actual detonation no longer reproduces the body-only overlap.");
                bool Died()=>Events(match).Any(e=>Kind(e)=="death"&&e.GetProperty("playerId").GetString()=="local-1"&&
                    e.GetProperty("reason").GetString()=="explosion");
                if(heart)
                {
                    Assert(!Player(match,1).GetProperty("heart").GetBoolean(),"Overhead blast failed to consume the victim's heart.");
                    Step(match,18);
                    Assert(!Died()&&Player(match,1).GetProperty("alive").GetBoolean(),"Overhead blast bypassed heart damage protection.");
                }
                else Assert(Died(),$"Overhead {pumps}-pump explosion reached the player's body but missed its feet and left it alive.");
                detonated=true;break;
            }
            Assert(detonated,$"Fixture never reached an overhead body overlap for {pumps} pumps.");
            Console.WriteLine($"PASS overhead {pumps}-pump explosion hits the player's body (practice={practice}, heart={heart})");count++;
        }
        void Check(string name,Action test){test();Console.WriteLine("PASS "+name);count++;}
        Check("player height intersects both halves of a blast without widening its horizontal reach",()=>
        {
            var stage=FlatArena([(1,1)]);var soft=new SoftBlockGrid(9,9);
            Assert(!BlastGeometry.Contains(stage,soft,3.5f,2.3f,3.5f,1.3f,3.5f,0,3.5f),"Fixture's feet are already inside the sphere.");
            Assert(BlastGeometry.HitsPlayer(stage,soft,3.5f,2.3f,3.5f,1.3f,3.5f,0,3.5f),"Lower blast hemisphere missed the head.");
            Assert(BlastGeometry.HitsPlayer(stage,soft,3.5f,1.8f,3.5f,1.3f,3.5f,3,3.5f),"Upper blast hemisphere missed an airborne player's feet.");
            Assert(!BlastGeometry.HitsPlayer(stage,soft,3.5f,2.6f,3.5f,1.3f,3.5f,0,3.5f),"Blast reached below its sphere.");
            Assert(!BlastGeometry.HitsPlayer(stage,soft,3.5f,0,3.5f,1.3f,4.81f,0,3.5f),"Player-height fix increased horizontal reach.");
            Assert(!BlastGeometry.HitsPlayer(stage,soft,3.5f,0,3.5f,1.3f,3.5f,2,3.5f),"Small ground blast hit a player two tiers overhead.");
        });
        Check("player-height blast tests retain stone, soft-cover and solid-deck shielding",()=>
        {
            var stage=FlatArena([(1,1)]);var soft=new SoftBlockGrid(9,9);
            stage.Cells[3,3]=new CellDef{Kind=CellKind.Floor,Tier=5};
            foreach(bool red in new[]{false,true})
                Assert(!BlastGeometry.HitsPlayer(stage,soft,2.5f,2.3f,3.5f,4,4.5f,0,3.5f,red),"Body hit passed through hard terrain.");
            stage.Cells[3,3]=CellDef.Floor0;soft.Set(3,3,true);
            Assert(!BlastGeometry.HitsPlayer(stage,soft,2.5f,.5f,3.5f,4,4.5f,0,3.5f),"Normal blast passed through cover.");
            Assert(BlastGeometry.HitsPlayer(stage,soft,2.5f,.5f,3.5f,4,4.5f,0,3.5f,true),"Red blast lost cover piercing.");
            soft.Set(3,3,false);
            stage.Cells[3,3]=new CellDef{Kind=CellKind.Floor,Tier=2,Below=CellDef.Floor0};
            foreach(bool red in new[]{false,true})
                Assert(!BlastGeometry.HitsPlayer(stage,soft,3.5f,2.5f,3.5f,2,3.5f,0,3.5f,red),"Overhead blast passed through a shielding deck.");
        });
        return count;
    }
    private static void CheckLateEntry(int pumps,bool heart,bool expired,int firePickups=0)
    {
        var items=new List<(int,int,string)>{(8,8,"remote")};
        if(heart)items.Add((8,19,"heart"));
        for(int i=0;i<firePickups;i++)items.Add((8,8,"fire"));
        var stage=new StageLayout{Id="fixture",Width=25,Height=25,Cells=FlatArena([(8,8)],25).Cells,
            Spawns=[(8,8),(8,19),(23,23)],SoftBlockDensity=0,InitialItems=items};
        var match=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0,Ghosts=false,SuddenDeath=false},stage);
        Fight(match);
        match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,2);
        for(int pump=0;pump<pumps;pump++)
        {
            match.SetInput(0,new PlayerInput(0,0,B:true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,16);
        }
        match.SetInput(0,new PlayerInput(0,0,B:true,AimTarget:new(8.5f,8.5f)));Step(match,2);
        match.SetInput(0,new PlayerInput(-1,0));Step(match,32);
        match.SetInput(0,default);Step(match,30);
        var bomb=State(match).GetProperty("bombs")[0];
        float bx=bomb.GetProperty("x").GetSingle(),bz=bomb.GetProperty("z").GetSingle();
        Assert(bomb.GetProperty("pumps").GetInt32()==pumps,"Late-entry bomb did not complete the requested pumps.");
        float radius=bomb.GetProperty("effectiveLevel").GetInt32()*.9f+.4f;
        // Approach to just outside the full-size sphere before ignition.
        match.SetInput(1,new PlayerInput(0,-1));
        for(int tick=0;tick<100&&Player(match,1).GetProperty("z").GetSingle()>bz+radius+.6f;tick+=2)Step(match,2);
        match.SetInput(1,default);Step(match,2);
        match.SetInput(0,new PlayerInput(0,0,R:true));Step(match,2);
        Assert(State(match).GetProperty("hazards").GetArrayLength()==1,"Remote did not create the late-entry blast: "+State(match));
        Assert(Player(match,1).GetProperty("alive").GetBoolean(),"Late-entry victim was already inside at ignition.");
        if(expired)Step(match,14);
        match.SetInput(1,new PlayerInput(0,-1));Step(match,8);
        match.SetInput(1,default);
        var victim=Player(match,1);var hazards=State(match).GetProperty("hazards");
        float dx=victim.GetProperty("x").GetSingle()-bx,dz=victim.GetProperty("z").GetSingle()-bz;
        Console.WriteLine($"WITNESS late-entry distance={MathF.Sqrt(dx*dx+dz*dz)} radius={radius} hazards={hazards} alive={victim.GetProperty("alive")}");
        Assert(dx*dx+dz*dz<radius*radius,"Late-entry victim never entered the sphere.");
        Assert((hazards.GetArrayLength()==0)==expired,"Blast lifetime does not match the entry fixture.");
        Assert(victim.GetProperty("alive").GetBoolean()==(heart||expired),"Player walked inside a still-active explosion and survived, or an expired/protected blast killed them.");
        if(heart)
        {
            Assert(victim.GetProperty("heart").GetBoolean()==expired,"Active bloom did not consume exactly one heart.");
            Step(match,18);
            Assert(Player(match,1).GetProperty("alive").GetBoolean(),"Lingering blast bypassed heart protection.");
        }
        if(!heart&&!expired)
            Assert(Events(match).Any(e=>Kind(e)=="death"&&e.GetProperty("playerId").GetString()=="local-1"&&
                e.GetProperty("causedBy").GetString()=="local-0"),"Late-entry kill lost the bomb owner's attribution.");
        Console.WriteLine($"PASS late-entry pumps={pumps} firePickups={firePickups} heart={heart} expired={expired}");
    }

    private static void CheckCoverDuringBloom(bool red)
    {
        var stage=new StageLayout{Id="fixture",Width=21,Height=21,Cells=FlatArena([(3,8)],21).Cells,
            Spawns=[(3,8),(6,8),(19,19)],SoftBlockDensity=0,FixedSoftBlocks=new HashSet<(int,int)>{(5,8)},
            InitialItems=[(3,8,red?"red":"remote"),(3,8,"fire"),(3,8,"fire"),(3,8,"fire")]};
        var match=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0,Ghosts=false,SuddenDeath=false},stage);
        Fight(match);
        match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);
        match.SetInput(0,default);Step(match,2);
        if(!red)match.SetInput(0,new PlayerInput(0,0,R:true));
        for(int ticks=0;ticks<100&&State(match).GetProperty("hazards").GetArrayLength()==0;ticks++)Step(match,1);
        Assert(State(match).GetProperty("hazards").GetArrayLength()==1,"Cover fixture did not detonate.");
        byte[] blocks=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
        Assert((blocks[(8*21+5)/8]&(1<<((8*21+5)%8)))==0,"Explosion did not destroy the shielding block.");
        Step(match,10);
        Assert(Player(match,1).GetProperty("alive").GetBoolean()!=red,
            "Lingering blast ignored original cover shielding or red piercing.");
        Console.WriteLine($"PASS original cover shields throughout bloom; red={red}");
    }

}
