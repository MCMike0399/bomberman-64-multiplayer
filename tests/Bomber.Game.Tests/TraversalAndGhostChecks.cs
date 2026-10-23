using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class TraversalAndGhostChecks
{
    public static int Run()
    {
        int n=0;void Check(string name,Action action){action();n++;Console.WriteLine("PASS "+name);}
        Check("walking off an upper ledge falls continuously and lands without stun",()=>{
            var m=Match(ledge:true);m.SetInput(0,new PlayerInput(-1,0));Step(m,5);var p=P(m);
            Assert(F(p,"x")<4,"Invisible boundary stopped the player walking down off a ledge.");
            Assert(F(p,"y")>0&&F(p,"y")<2&&p.GetProperty("airborne").GetBoolean(),"Downward ledge traversal teleported instead of falling.");
            m.SetInput(0,default);Step(m,25);p=P(m);
            Assert(F(p,"y")==0&&!p.GetProperty("airborne").GetBoolean()&&!p.GetProperty("stunned").GetBoolean(),"Ordinary falling did not land freely on the lower floor.");
            float x=F(p,"x");m.SetInput(0,new PlayerInput(-1,0));Step(m,6);
            Assert(F(P(m),"x")<x-.5f,"Landing beside the cliff trapped the player inside its side.");
        });
        Check("falling keeps full air steering and upper faces remain solid",()=>{
            var m=Match(ledge:true);m.SetInput(0,new PlayerInput(-1,0));Step(m,5);
            float z=F(P(m),"z");m.SetInput(0,new PlayerInput(0,1));Step(m,5);
            Assert(F(P(m),"z")>z+.5f,"Falling player lost horizontal steering beside the ledge. "+P(m));
            m.SetInput(0,new PlayerInput(1,0));Step(m,6);
            Assert(F(P(m),"x")<4,"Falling player climbed back through the upper cliff face.");
        });
        Check("AI walk edges allow dropping down and forbid climbing the same cliff",()=>{
            var cells=new CellDef[2,1];cells[0,0]=CellDef.Floor0;cells[1,0]=CellDef.Floor2;
            var stage=new StageLayout {Id="fixture",Width=2,Height=1,Cells=cells,Spawns=[(0,0)]};
            Assert(stage.CanWalk(1,0,0,0)&&!stage.CanWalk(0,0,1,0),"Cliff movement graph is not directed downwards.");
        });
        Check("falling into water eliminates at water height and stone still blocks",()=>{
            var water=Match(ledge:true,water:true);water.SetInput(0,new PlayerInput(-1,0));Step(water,5);
            Assert(!P(water).GetProperty("ghost").GetBoolean()&&F(P(water),"y")>0,"Water killed the falling player at the upper lip.");
            water.SetInput(0,default);Step(water,25);
            Assert(P(water).GetProperty("ghost").GetBoolean(),"Falling into water failed to eliminate at its surface.");
            var stone=Match(ledge:true,stone:true);stone.SetInput(0,new PlayerInput(-1,0));Step(stone,20);
            Assert(F(P(stone),"x")>=3.39f,"Falling player passed through a stone pillar.");
        });
        Check("low shoreline crossing falls to the published water surface before drowning",()=>{
            var m=Match(water:true);m.SetInput(0,new PlayerInput(-1,0));Step(m,5);var p=P(m);
            Assert(!p.GetProperty("ghost").GetBoolean()&&p.GetProperty("airborne").GetBoolean(),
                "Crossing a low shoreline killed the player before reaching water: "+p);
            Assert(F(p,"y")>State(m).GetProperty("waterLevel").GetSingle(),"Fixture was already submerged.");
            m.SetInput(0,default);Step(m,25);
            Assert(P(m).GetProperty("ghost").GetBoolean(),"Actual water contact failed to drown.");
        });
        Check("shoreline edge and corner grazing keep grounded feet alive",()=>{
            foreach(bool corner in new[]{false,true})
            {
                var m=Match(water:true,corner:corner);
                m.SetInput(0,new PlayerInput(-.3f,0));Step(m,10);
                if(corner){m.SetInput(0,new PlayerInput(0,-1));Step(m,9);}
                m.SetInput(0,new PlayerInput(0,.1f));Step(m,12);
                m.SetInput(0,default);Step(m,60);var p=P(m);
                Assert(F(p,"x")>2&&F(p,"x")<2.4f&&!p.GetProperty("ghost").GetBoolean()&&
                    !p.GetProperty("airborne").GetBoolean()&&F(p,"y")==0,
                    "Collision-radius grazing drowned a center still supported by land: "+p);
            }
        });
        Check("upper shoreline fall survives below tier zero and ends exactly at water height",()=>{
            var m=Match(ledge:true,water:true);m.SetInput(0,new PlayerInput(-1,0));Step(m,5);m.SetInput(0,default);
            bool belowFloor=false;for(int i=0;i<30&&!P(m).GetProperty("ghost").GetBoolean();i++)
            {
                var p=P(m);if(F(p,"y")<0)belowFloor=true;
                Step(m,2);
            }
            Assert(belowFloor,"Upper shoreline fall drowned at the water cell's fictitious tier-zero floor.");
            Assert(P(m).GetProperty("ghost").GetBoolean()&&State(m).GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&F(e,"y")==State(m).GetProperty("waterLevel").GetSingle()),
                "Water elimination did not occur on the published water plane.");
        });
        Check("void crossing falls below ground and remains lethal",()=>{
            var m=Match(water:true,voidEdge:true);m.SetInput(0,new PlayerInput(-1,0));Step(m,5);
            Assert(P(m).GetProperty("airborne").GetBoolean()&&!P(m).GetProperty("ghost").GetBoolean(),"Void killed at an imaginary floor.");
            m.SetInput(0,default);Step(m,25);
            Assert(P(m).GetProperty("ghost").GetBoolean()&&State(m).GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&F(e,"y")<0),"Void no longer eliminates a real fall.");
        });
        Check("rising water stays below feet until contact and still drowns grounded players",()=>{
            var cells=new CellDef[11,11];for(int x=0;x<11;x++)for(int z=0;z<11;z++)cells[x,z]=x>=4?CellDef.Floor2:CellDef.Floor0;
            var m=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,RoundSeconds=60},
                new StageLayout {Id=BlastStages.SunkenTemple,Width=11,Height=11,Cells=cells,Spawns=[(2,3),(8,7),(9,9)],SoftBlockDensity=0});
            Step(m,214);
            Assert(State(m).GetProperty("waterLevel").GetSingle()<0&&!P(m).GetProperty("ghost").GetBoolean()&&P(m).GetProperty("alive").GetBoolean(),"Flood drowned dry feet.");
            Step(m,180);
            Assert(!P(m).GetProperty("alive").GetBoolean()&&State(m).GetProperty("waterLevel").GetSingle()>=0,"Flood contact stopped being lethal.");
            Assert(P(m,1).GetProperty("alive").GetBoolean(),"Flood incorrectly drowned the higher terrace.");
        });
        Check("latched ghost cannot replace victim movement and follows the final position",()=>{
            var m=Ghost();float x=F(P(m,1),"x");m.SetInput(0,new PlayerInput(-1,0));m.SetInput(1,new PlayerInput(1,0));Step(m,6);
            Assert(F(P(m,1),"x")>x+.4f,"Ghost completely overrode the victim's own movement.");
            Assert(Math.Abs(F(P(m),"x")-F(P(m,1),"x"))<.02f&&Math.Abs(F(P(m),"z")-F(P(m,1),"z"))<.02f,"Latched ghost moved independently instead of following victim.");
            m.SetInput(0,default);m.SetInput(1,default);Step(m,125);
            Assert(P(m).GetProperty("latchedTo").ValueKind==JsonValueKind.Null,"Ghost latch never expired.");
            Assert(State(m).GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="ghost-detach"),"Ghost detach lacked an authoritative event.");
        });
        Check("victim direction reversals shake off the ghost before timeout",()=>{
            var m=Ghost();m.SetInput(0,default);
            for(int i=0;i<7;i++){m.SetInput(1,new PlayerInput(i%2==0?1:-1,0));Step(m,6);}
            Assert(P(m).GetProperty("latchedTo").ValueKind==JsonValueKind.Null,"Victim reversals failed to shake off the ghost.");
        });
        return n;
    }
    static LocalMatch Match(bool ledge=false,bool water=false,bool stone=false,bool corner=false,bool voidEdge=false)
    {
        var cells=new CellDef[11,11];for(int x=0;x<11;x++)for(int z=0;z<11;z++)cells[x,z]=ledge&&x>=4?CellDef.Floor2:CellDef.Floor0;
        if(water)for(int x=0;x<11;x++)for(int z=0;z<11;z++)
            if(x<(ledge?4:2)||corner&&z<2)cells[x,z]=voidEdge?CellDef.Void:CellDef.Water;
        if(stone)cells[2,3]=CellDef.Pillar;
        var m=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0},new StageLayout {Id="fixture",Width=11,Height=11,Cells=cells,Spawns=ledge?[(4,3),(8,7),(9,9)]:[(2,3),(4,3),(9,9)],SoftBlockDensity=0});Step(m,64);return m;
    }
    static LocalMatch Ghost()
    {
        var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);m.SetInput(0,default);Step(m,90);
        Assert(P(m).GetProperty("ghost").GetBoolean(),"Fixture failed to create a real ghost through explosion.");
        m.SetInput(0,new PlayerInput(1,0));
        for(int i=0;i<20&&P(m).GetProperty("latchedTo").ValueKind==JsonValueKind.Null;i++)m.Tick();
        Assert(P(m).GetProperty("latchedTo").GetString()=="local-1","Fixture failed to latch a living player.");return m;
    }
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement P(LocalMatch m,int i=0)=>State(m).GetProperty("players")[i];
    static float F(JsonElement e,string k)=>e.GetProperty(k).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
