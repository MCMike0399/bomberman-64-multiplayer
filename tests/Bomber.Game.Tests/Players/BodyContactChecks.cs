using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class BodyContactChecks
{
    public static int Run()
    {
        int count=0;
        void Check(string name,Action test){test();Console.WriteLine("PASS "+name);count++;}
        Check("opposing inputs bodyblock without overlap or swapping sides",()=>{
            var m=Match([(3,5),(6,5)]);m.SetInput(0,new PlayerInput(1,0));m.SetInput(1,new PlayerInput(-1,0));
            for(int i=0;i<45;i++){m.Tick();Separated(m);Assert(X(m,0)<X(m,1),"Head-on players crossed through one another.");}
            Assert(Math.Abs((X(m,0)+X(m,1))/2-5)<.03f,"Head-on pair favored a seat.");
        });
        Check("stationary opponent yields slowly under sustained body pressure",()=>{
            var m=Match([(3,5),(5,5)]);m.SetInput(0,new PlayerInput(1,0));Step(m,30);Separated(m);
            Assert(X(m,1)>5.6f&&X(m,1)<6.7f,"Stationary body did not yield at a bounded speed.");
            Assert(X(m,0)<X(m,1),"Pusher crossed stationary opponent.");
        });
        Check("diagonal body glance preserves tangential escape",()=>{
            var m=Match([(3,5),(5,5)]);m.SetInput(0,new PlayerInput(1,.45f));
            for(int i=0;i<25;i++){m.Tick();Separated(m);}
            Assert(Z(m,0)>6.5f&&X(m,0)>6,"Diagonal player became stuck on body contact.");
        });
        Check("body pressure against stone cannot force overlap or wall penetration",()=>{
            var m=Match([(3,5),(5,5)],wall:true);m.SetInput(0,new PlayerInput(1,0));
            for(int i=0;i<90;i++){m.Tick();Separated(m);Assert(X(m,1)<=5.601f,"Body pushed through stone.");}
            m.SetInput(1,new PlayerInput(0,1));Step(m,10);Separated(m);
            Assert(Z(m,1)>6.8f,"Pinned body could not slide out while the attacker kept pressing.");
            m.SetInput(0,new PlayerInput(0,1));Step(m,15);Assert(Z(m,0)>6.8f,"Corner pressure permanently trapped the pusher.");
        });
        Check("eight converging player inputs retain pair separation",()=>{
            var spawns=new (int,int)[]{(3,3),(5,3),(7,3),(3,5),(7,5),(3,7),(5,7),(7,7)};
            var m=Match(spawns);for(int i=0;i<8;i++)m.SetInput(i,new PlayerInput(Math.Sign(5-spawns[i].Item1),Math.Sign(5-spawns[i].Item2)));
            for(int i=0;i<80;i++){m.Tick();Separated(m);}
        });
        Check("co-located spawn repair separates idle bodies without a state edit",()=>{
            var m=Match([(4,5),(4,5)]);Separated(m);
        });
        Check("players on separate terrace heights do not block one another",()=>{
            var m=Match([(3,5),(5,5)],upper:true);m.SetInput(1,new PlayerInput(-1,0));Step(m,10);
            Assert(X(m,1)<4.6f,"Upper player could not fall past lower body.");
        });
        return count;
    }
    static LocalMatch Match((int,int)[] spawns,bool wall=false,bool upper=false)
    {
        var cells=new CellDef[13,13];for(int x=0;x<13;x++)for(int z=0;z<13;z++)cells[x,z]=upper&&x>=5?CellDef.Floor2:CellDef.Floor0;
        if(wall)for(int z=0;z<13;z++)cells[6,z]=CellDef.Pillar;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=spawns.Length,BotPlayers=0,Practice=true},new StageLayout{Id="body-fixture",Width=13,Height=13,Cells=cells,Spawns=spawns,SoftBlockDensity=0});Step(m,64);return m;
    }
    static JsonElement Players(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").GetProperty("players").Clone();}
    static float X(LocalMatch m,int i)=>Players(m)[i].GetProperty("x").GetSingle();
    static float Z(LocalMatch m,int i)=>Players(m)[i].GetProperty("z").GetSingle();
    // Inspect authority without editing state: wire positions round to .01 cells,
    // which is too coarse to assert exact circle clearance in diagonal crowds.
    static void Separated(LocalMatch m)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var session=typeof(LocalMatch).GetField("_session",flags)!.GetValue(m)!;
        var players=(Dictionary<string,BlastPlayer>)typeof(BlastSession).GetField("_players",flags)!.GetValue(session)!;
        var p=players.Values.Where(p=>p.Alive&&!p.Ghost&&p.CarriedById is null).ToArray();
        for(int i=0;i<p.Length;i++)for(int j=i+1;j<p.Length;j++)
        {
            if(Math.Abs(p[i].Y-p[j].Y)>=1.15f)continue;
            float dx=p[i].X-p[j].X,dz=p[i].Z-p[j].Z;
            Assert(dx*dx+dz*dz>=.7998f*.7998f,$"Bodies {i}/{j} overlap in authority: {MathF.Sqrt(dx*dx+dz*dz)}.");
        }
    }
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
