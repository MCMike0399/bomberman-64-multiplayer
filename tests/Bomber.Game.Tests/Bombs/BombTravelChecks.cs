using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class BombTravelChecks
{
    public static int Run()
    {
        int n=0;var failures=new List<string>();
        void Check(string name,Action test){try{test();Console.WriteLine("PASS "+name);n++;}catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+failures[^1]);}}
        Check("kick falls from terrace and resumes its full speed across lower grass",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:CellDef.Floor0);Kick(m);bool falling=false,resumed=false;float previousY=2;
            for(int i=0;i<60;i++){
                Step(m,1);var b=Bomb(m);float y=F(b,"y");
                if(b.GetProperty("state").GetInt32()==3){falling=true;Assert(y<=previousY+.02f,"Ground shot gained an overhead lob while falling.");}
                if(falling&&b.GetProperty("state").GetInt32()==1)resumed=true;
                previousY=y;
            }
            Assert(falling,"Downward terrace edge stopped the shot instead of starting gravity.");
            Assert(resumed&&F(Bomb(m),"z")>28,"Landing discarded the kick momentum before a two-second trip.");
        });
        Check("kick preserves momentum through successive terrace drops",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:z<19?CellDef.Floor1:CellDef.Floor0);Kick(m);int drops=0;bool wasFalling=false;
            for(int i=0;i<60;i++){Step(m,1);bool falling=Bomb(m).GetProperty("state").GetInt32()==3;if(falling&&!wasFalling)drops++;wasFalling=falling;}
            Assert(drops==2&&F(Bomb(m),"z")>28&&Bomb(m).GetProperty("state").GetInt32()==1,"A second terrace drop lost the sustained ground shot.");
        });
        Check("falling kick retains the usual opponent knockup on lower ground",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:CellDef.Floor0,victim:true);Kick(m);bool launched=false;
            for(int i=0;i<45;i++){Step(m,1);var p=State(m).GetProperty("players")[1];
                if(p.GetProperty("airborne").GetBoolean()&&F(p,"y")>.08f&&F(p,"z")>14.6f)launched=true;}
            Assert(launched,"Terrain flight lost the contact kick impulse against a lower opponent.");
            Assert(F(Bomb(m),"z")<15.3f,"Falling kick passed freely through its victim.");
        });
        Check("kick crosses connected ramp mound then continues across grass",()=>{
            var m=Match((x,z)=>z==8?CellDef.Ramp0To1(0,1):z>=9&&z<12?CellDef.Floor1:z==12?CellDef.Ramp0To1(0,-1):CellDef.Floor0);
            Kick(m);Step(m,60);Assert(F(Bomb(m),"z")>28&&Bomb(m).GetProperty("state").GetInt32()==1,"Connected mound shortened the useful kick range.");
        });
        Check("kick cannot climb a vertical terrace wall",()=>{
            var m=Match((x,z)=>z>=8?CellDef.Floor2:CellDef.Floor0);Kick(m);Step(m,25);
            Assert(F(Bomb(m),"z")<=8-BlastBomb.Radius+.01f&&Bomb(m).GetProperty("state").GetInt32()==0,"Kick climbed or penetrated the uphill wall.");
        });
        Check("falling kicked bomb sweeps its full radius against a narrow pillar",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:z==11&&x==3?CellDef.Pillar:CellDef.Floor0);Kick(m);RequireFall(m,40);
            Assert(F(Bomb(m),"z")<=11-BlastBomb.Radius+.01f,"Falling bomb tunneled through its pillar.");
        });
        Check("falling kicked bomb cannot tunnel through a diagonal pillar corner",()=>{
            var m=Match((x,z)=>x<8&&z<8?CellDef.Floor2:x==11&&z==11?CellDef.Pillar:CellDef.Floor0);
            m.SetInput(0,new PlayerInput(1,1));Step(m,2);Kick(m,diagonal:true);RequireFall(m,45);var b=Bomb(m);
            float dx=Math.Max(0,11-F(b,"x")),dz=Math.Max(0,11-F(b,"z"));
            Assert(F(b,"x")<11&&F(b,"z")<11&&dx*dx+dz*dz>=BlastBomb.Radius*BlastBomb.Radius-.02f,"Diagonal flying circle penetrated corner stone.");
        });
        Check("falling kick stops at an uphill wall before it can land inside the terrace",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:z>=11?CellDef.Floor2:CellDef.Floor0);Kick(m);RequireFall(m,40);
            Assert(F(Bomb(m),"z")<=11-BlastBomb.Radius+.01f&&Bomb(m).GetProperty("state").GetInt32()==0,"Airborne kick tunneled through an uphill terrace.");
        });
        Check("kick remains airborne over water until reaching the actual water plane",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor0:CellDef.Water);Kick(m);bool belowFloor=false;
            for(int i=0;i<40;i++){Step(m,1);var bombs=State(m).GetProperty("bombs");if(bombs.GetArrayLength()==0)break;
                var b=bombs[0];if(F(b,"z")>=8&&F(b,"y")<-.1f)belowFloor=true;}
            Assert(belowFloor,"Water cell metadata at tier zero destroyed the bomb above the actual water plane.");
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Water contact did not remove the falling kick.");
        });
        Check("normal fuse keeps counting throughout terrace flight and landing",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:CellDef.Floor0,remote:false);Kick(m);Step(m,80);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Falling or landing paused/restarted the normal fuse.");
        });
        Check("stop kick belongs to the kicker and preserves normal fuse",()=>{
            var m=Match((x,z)=>CellDef.Floor0,remote:false);Kick(m);Step(m,8);
            m.SetInput(1,new PlayerInput(0,0,Stop:true));Step(m,2);
            Assert(Bomb(m).GetProperty("state").GetInt32()==1,"Another player stopped the kicker's shot.");
            m.SetInput(0,new PlayerInput(0,0,Stop:true));Step(m,2);var b=Bomb(m);float z=F(b,"z"),fuse=F(b,"fuse");
            Assert(b.GetProperty("state").GetInt32()==0,"Stop did not halt rolling bomb.");Step(m,8);
            Assert(F(Bomb(m),"z")==z&&F(Bomb(m),"fuse")<fuse,"Stopped bomb moved or paused its fuse.");Step(m,80);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Stopped timed bomb never exploded.");
        });
        Check("stop falling kick removes drift while gravity and remote control survive",()=>{
            var m=Match((x,z)=>z<8?CellDef.Floor2:CellDef.Floor0);Kick(m);
            for(int i=0;i<30&&Bomb(m).GetProperty("state").GetInt32()!=3;i++)Step(m,1);
            Assert(Bomb(m).GetProperty("state").GetInt32()==3,"Fixture failed to reach falling kick.");
            m.SetInput(0,new PlayerInput(0,0,Stop:true));Step(m,2);float z=F(Bomb(m),"z");Step(m,40);
            Assert(Bomb(m).GetProperty("state").GetInt32()==0&&F(Bomb(m),"y")==0&&F(Bomb(m),"z")==z,"Stop left falling bomb suspended or drifting.");
            Assert(Bomb(m).GetProperty("remote").GetBoolean(),"Stop changed remote mode.");m.SetInput(0,new PlayerInput(0,0,R:true));Step(m,2);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Stopped remote ignores detonation.");
        });
        Check("ordinary carried throw lands resting without acquiring kick momentum",()=>{
            var m=Match((x,z)=>CellDef.Floor0);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,10);m.SetInput(0,default);Step(m,2);
            Assert(Bomb(m).GetProperty("state").GetInt32()==2,"Throw fixture did not lift its bomb.");
            m.SetInput(0,new PlayerInput(0,1,B:true));Step(m,2);m.SetInput(0,default);Step(m,35);
            Assert(Bomb(m).GetProperty("state").GetInt32()==0,"Ordinary throw acquired endless rolling on landing.");
            float z=F(Bomb(m),"z");Step(m,10);Assert(Math.Abs(F(Bomb(m),"z")-z)<.01f,"Landed throw kept moving.");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));return n;
    }
    static LocalMatch Match(Func<int,int,CellDef> cell,bool remote=true,bool victim=false){var cells=new CellDef[40,45];for(int x=0;x<40;x++)for(int z=0;z<45;z++)cells[x,z]=cell(x,z);cells[35,40]=CellDef.Floor0;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="bomb-travel",Width=40,Height=45,Cells=cells,Spawns=[(3,3),victim?(3,14):(35,40)],SoftBlockDensity=0,InitialItems=remote?[(3,3,"remote")]:[]});Step(m,64);return m;}
    static void Kick(LocalMatch m,bool diagonal=false){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);m.SetInput(0,new PlayerInput(diagonal?1:0,1,A:true));
        for(int i=0;i<15;i++){Step(m,1);if(Bomb(m).GetProperty("state").GetInt32()!=0){m.SetInput(0,default);return;}}
        throw new InvalidOperationException("Fixture did not launch kick.");}
    static void RequireFall(LocalMatch m,int ticks){bool falling=false;for(int i=0;i<ticks;i++){Step(m,1);falling|=Bomb(m).GetProperty("state").GetInt32()==3;}Assert(falling,"Collision fixture never reached the falling state.");}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Bomb(LocalMatch m)=>State(m).GetProperty("bombs")[0];
    static float F(JsonElement e,string k)=>e.GetProperty(k).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
