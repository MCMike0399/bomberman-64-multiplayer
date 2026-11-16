using System.Numerics;
using System.Reflection;
using Bomber.Game.Matches;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using static GameFixtures;

internal static class InTheGutterChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(string name,Action test){test();checks++;Console.WriteLine("PASS In the Gutter "+name);}
        var stage=BlastStages.Get(BlastStages.InTheGutter);
        Check("shallow safe court, five posts and eight starts",()=>
        {
            Assert(stage.Id==BlastStages.InTheGutter&&stage.Width==21&&stage.Height==21,"Unknown stage fell back to another map.");
            Assert(!stage.RisingWater&&stage.Spawns.Distinct().Count()==8,"Current court needs eight safe starts without a flood.");
            Assert(stage.Cells.Cast<CellDef>().All(c=>!c.IsElimination),"Shallow court accidentally drowns players.");
            Assert(stage.Spawns.Take(4).All(c=>stage.SurfaceAt(c.X+.5f,c.Z+.5f)==2),"First four starts must occupy the tall post caps.");
            Assert(stage.At(10,10).Kind==CellKind.Pillar&&stage.At(10,10).ObstacleHeight==1.1f,"Center flow-marker post is missing.");
            Assert(stage.Spawns.Skip(4).All(c=>stage.At(c.X,c.Z).IsPassable&&stage.At(c.X,c.Z).Tier==0),"Extra seats need ground-level routes.");
        });
        Check("cover settings and connected spawn exits",()=>
        {
            foreach(int percent in new[]{0,50,100})
            {
                var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=8,BotPlayers=0,Seed=64,Advanced=new AdvancedRules{BlockPercent=percent}});
                var bits=Convert.FromBase64String(State(match).GetProperty("softBlocks").GetString()!);
                int count=bits.Sum(b=>BitOperations.PopCount((uint)b));
                Assert(count==48*percent/100,"Waterworks cover density ignores advanced settings.");
                foreach(var spawn in stage.Spawns)
                    Assert((bits[(spawn.Z*stage.Width+spawn.X)/8]&(1<<((spawn.Z*stage.Width+spawn.X)%8)))==0,"Cover occupies a spawn.");
            }
        });
        Check("flow is seeded bounded lateral water with calm",()=>
        {
            var states=new HashSet<int>();
            for(int tick=0;tick<6000;tick++)
            {
                float flow=GutterRules.Current(tick/30f,64,1);
                Assert(float.IsFinite(flow)&&Math.Abs(flow)<=GutterRules.CurrentSpeed,"Current exceeds its published budget.");
                Assert(flow==GutterRules.Current(tick/30f,64,1),"Current schedule is nondeterministic.");
                states.Add(Math.Sign(flow));
            }
            Assert(states.SetEquals([-1,0,1]),"Current never changes direction or calms.");
        });
        Check("current moves floor players but leaves post tops still",()=>
        {
            var match=Match();Fight(match);var session=Session(match);
            var p=session.AiTestPlayers.First();session.AiTestPosition(p.Id,10.5f,5.5f);
            float x=p.X;Step(match,30);
            Assert(p.Alive&&p.X<x-.5f&&Math.Abs(p.Z-5.5f)<.01f,"Safe shallow current fails to carry an idle player.");
            var elevated=session.AiTestPlayers.Skip(1).First();float topX=elevated.X,topZ=elevated.Z;
            Step(match,30);
            Assert(elevated.X==topX&&elevated.Z==topZ&&elevated.Y==2,"Current pushes a dry elevated start.");
        });
        Check("ordinary movement drops safely from a post",()=>
        {
            var match=Match();Fight(match);match.SetInput(0,new PlayerInput(0,1));Step(match,35);match.SetInput(0,default);
            Assert(Player(match).GetProperty("alive").GetBoolean()&&Player(match).GetProperty("y").GetSingle()==0,"Dropping into shallow water kills or leaves the player floating.");
        });
        Check("current advects free bombs without changing fuse or held attachment",()=>
        {
            var match=Match();Fight(match);var session=Session(match);
            var bomb=Bomb("free",10.5f,5.5f);session.AiTestAddBomb(bomb);Step(match,30);
            Assert(bomb.X<10&&Math.Abs(bomb.Z-5.5f)<.001f,"A free bomb does not follow lateral current.");
            Assert(Math.Abs(bomb.FuseTimer-9)<.01f,"Current alters the real bomb clock.");
            var cap=Bomb("on-cap",4.5f,4.5f);cap.Tier=2;cap.LandingY=2;session.AiTestAddBomb(cap);
            Step(match,12);Assert(cap.X==4.5f&&cap.Z==4.5f,"A dry post-top bomb is pushed by water.");
            match.SetInput(1,new PlayerInput(0,0,A:true,B:true));Step(match,3);
            var carrier=session.AiTestPlayers.Skip(1).First();
            Assert(carrier.CarryingBombId is not null,"Ordinary button input did not lift a real bomb.");
            var held=session.AiTestBombs.Single(b=>b.Id==carrier.CarryingBombId);Step(match,12);
            Assert(held.State==BombState.Carried&&Math.Abs(held.X-carrier.X)<.01f,"Water separates a held bomb from its carrier.");
        });
        Check("posts and bodies obstruct bomb advection",()=>
        {
            var match=Match();Fight(match);var session=Session(match);
            var bomb=Bomb("post-side",6.5f,4.5f);session.AiTestAddBomb(bomb);Step(match,30);
            Assert(bomb.X>=6+BlastBomb.Radius-.01f,"Grounded bomb passes through a tall post.");
            var owner=session.AiTestPlayers.First();session.AiTestPosition(owner.Id,11.5f,8.5f);
            // Negative current cannot deepen the initial overlap with the downstream body.
            var near=Bomb("body-side",11.9f,8.5f);near.OwnerId=owner.Id;near.OwnerMayLeave=true;session.AiTestAddBomb(near);
            float gap=near.X-owner.X;
            for(int i=0;i<12;i++){match.Tick();Assert(near.X-owner.X>=gap-.001f||near.X-owner.X>=.74f,"Current drags a bomb through its owner.");gap=near.X-owner.X;}
        });
        Check("airborne throws do not inherit shallow water force",()=>
        {
            var match=Match();Fight(match);var session=Session(match);
            var bomb=Bomb("air",10.5f,5.5f);bomb.State=BombState.Flying;bomb.FlyY=3;bomb.FlyVY=5;bomb.FlyVZ=2;
            session.AiTestAddBomb(bomb);Step(match,10);
            Assert(Math.Abs(bomb.X-10.5f)<.001f,"Water current incorrectly bends an airborne throw.");
        });
        Check("closing grates publish columns and continuously eliminate swept players",()=>
        {
            var match=new LocalMatch(new MatchOptions{Stage=stage.Id,HumanPlayers=8,BotPlayers=0,Seed=64,RoundSeconds=60,Advanced=new AdvancedRules{BlockPercent=0}});
            Step(match,100);var state=State(match);
            Assert(state.GetProperty("hurryMode").GetString()=="columns"&&state.GetProperty("closedLeft").GetInt32()>=1,"Closing grates did not begin on both sides.");
            var session=Session(match);var p=session.AiTestPlayers.First();session.AiTestPosition(p.Id,.5f,8.5f);Step(match,2);
            Assert(!p.Alive,"Current can carry a living player into an already closed column.");
        });
        Console.WriteLine($"Passed {checks} In the Gutter integration checks.");
        return checks;
    }
    private static LocalMatch Match()=>new(new MatchOptions{Stage=BlastStages.InTheGutter,HumanPlayers=2,BotPlayers=0,Seed=64,Practice=true,Advanced=new AdvancedRules{BlockPercent=0}});
    private static BlastSession Session(LocalMatch match)=>(BlastSession)typeof(LocalMatch).GetField("_session",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(match)!;
    private static BlastBomb Bomb(string id,float x,float z)=>new(){Id=id,X=x,Z=z,CellX=(int)x,CellZ=(int)z,FuseTimer=10,LandingY=0};
}
