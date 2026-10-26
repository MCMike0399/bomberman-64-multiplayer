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
}
