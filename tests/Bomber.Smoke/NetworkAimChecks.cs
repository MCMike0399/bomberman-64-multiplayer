using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class NetworkAimChecks
{
    public static async Task Run(Uri origin)
    {
        var settings=new SessionSettings(Stage:"rock-garden",Bots:0,SuddenDeath:false);
        await using var host=await NetworkSession.ConnectAsync(origin,"Precision aim host",settings);
        await using var observer=await NetworkSession.ConnectAsync(origin,"Precision aim observer",settings,NetworkMode.JoinCode,host.JoinCode);
        await PartyNetworkChecks.Start(host);
        var id=host.PlayerId!;
        var ready=await Wait(observer,frame=>Data(frame).GetProperty("phase").GetInt32()==1);
        var initial=Player(ready,id);var x=F(initial,"x");var z=F(initial,"z");

        // Ordinary production encoder and live WebSocket transport, never raw smoke packets.
        host.SendInput(0,new PlayerInput(0,0,A:true,B:true));
        var lifted=await Wait(observer,frame=>Player(frame,id).GetProperty("carrying").GetString()=="bomb");
        var heldFuse=F(Bomb(lifted,id),"fuse");
        host.SendInput(0,new PlayerInput(0,1,B:true,Aim:true));
        var adjusted=await Wait(observer,frame=>{
            var p=Player(frame,id);
            return p.GetProperty("aiming").GetBoolean()&&p.GetProperty("aimSet").GetBoolean()&&
                F(p,"aimRange")>=BombThrowRules.MaxAimRange-.001f&&F(p,"aimZ")>.99f;
        },frame=>AssertPlanted(frame,id,x,z));
        host.SendInput(0,new PlayerInput(0,0,B:true,Aim:true));
        var stable=await Wait(observer,frame=>Tick(frame)>=Tick(adjusted)+6,frame=>AssertPlanted(frame,id,x,z));
        var stablePlayer=Player(stable,id);
        Check(F(Bomb(stable,id),"fuse")==heldFuse,"network carry keeps the original paused fuse");
        Check(float.IsFinite(F(stablePlayer,"aimX"))&&float.IsFinite(F(stablePlayer,"aimZ")),"network accepts finite deliberate direction");

        // Leaving the stance keeps intent. A fresh tick barrier excludes buffered earlier snapshots.
        host.SendInput(0,new PlayerInput(0,0,B:true));
        var releasedAim=await Wait(observer,frame=>Tick(frame)>Tick(stable)&&!Player(frame,id).GetProperty("aiming").GetBoolean());
        var persisted=Player(releasedAim,id);
        Check(persisted.GetProperty("aimSet").GetBoolean()&&SameAim(stablePlayer,persisted),"network aim direction and range persist after modifier release");
        var local=await LocalAcceptance(settings);
        foreach(var property in new[]{"aiming","aimSet","aimX","aimZ","aimRange"})
            Check(local.GetProperty(property).ValueKind==persisted.GetProperty(property).ValueKind,"local/network aim snapshot contract agrees: "+property);
        Check(Math.Abs(F(local,"aimRange")-F(persisted,"aimRange"))<.001f,"local and encoded network input reach the same bounded range");

        host.SendInput(0,default);
        var thrown=await Wait(observer,frame=>{
            var p=Player(frame,id);var b=FindBomb(frame,id);
            return p.GetProperty("carrying").ValueKind==JsonValueKind.Null&&b is {} bomb&&bomb.GetProperty("state").GetInt32()==3;
        });
        var flying=Bomb(thrown,id);
        Check(F(flying,"fuse")>0&&F(flying,"fuse")<heldFuse,"network carry release launches with a finite resumed fuse");
        Check(!Player(thrown,id).GetProperty("aimSet").GetBoolean(),"network throw clears deliberate intent");
        bool aboveHead=F(flying,"y")>F(Player(thrown,id),"y")+1.2f;
        bool crossedHead=false;
        await Wait(observer,frame=>{
            var p=Player(frame,id);var b=FindBomb(frame,id);
            Check(!p.GetProperty("stunned").GetBoolean()&&p.GetProperty("alive").GetBoolean(),"network deliberate lob does not hit its stationary owner");
            if(b is not {} bomb)return false;
            aboveHead|=F(bomb,"y")>F(p,"y")+1.2f;
            crossedHead|=aboveHead&&F(bomb,"y")<=F(p,"y")+1.2f;
            return crossedHead&&bomb.GetProperty("state").GetInt32()==0;
        });
        Check(aboveHead&&crossedHead,"network throw crosses descending head height before resting");
        Console.WriteLine("PASS production NetworkSession precision aim: planted stance, local protocol parity, retained intent, finite release fuse and safe descending head height");
    }

    private static async Task<JsonElement> LocalAcceptance(SessionSettings settings)
    {
        await using var local=new LocalSession(settings,humanCount:2);
        void Step(int ticks){for(int i=0;i<ticks;i++)local.Advance(1d/30);}
        Step(64);
        local.SendInput(0,new PlayerInput(0,0,A:true,B:true));Step(2);
        local.SendInput(0,new PlayerInput(0,1,B:true,Aim:true));Step(48);
        local.SendInput(0,new PlayerInput(0,0,B:true));Step(2);
        var state=local.DrainMessages().Last(m=>m.GetProperty("t").GetString()=="state");
        var p=Player(state,"local-0");
        Check(!p.GetProperty("aiming").GetBoolean()&&p.GetProperty("aimSet").GetBoolean(),"local input accepts and retains precision aim");
        Check(F(p,"x")==1.5f&&F(p,"z")==1.5f,"local precision stance stays planted");
        return p.Clone();
    }
    private static bool SameAim(JsonElement a,JsonElement b)=>new[]{"aimX","aimZ","aimRange"}.All(key=>Math.Abs(F(a,key)-F(b,key))<.0001f);
    private static void AssertPlanted(JsonElement frame,string id,float x,float z)
    {
        var p=Player(frame,id);
        Check(Math.Abs(F(p,"x")-x)<.001f&&Math.Abs(F(p,"z")-z)<.001f,"network aim consumes stick intent without moving the carrier");
    }
    private static async Task<JsonElement> Wait(NetworkSession session,Func<JsonElement,bool> accepted,Action<JsonElement>? inspect=null)
    {
        var deadline=Stopwatch.StartNew();
        while(deadline.Elapsed<TimeSpan.FromSeconds(15))
        {
            foreach(var message in session.DrainMessages())
            {
                if(message.GetProperty("t").GetString()!="state")continue;
                inspect?.Invoke(message);
                if(accepted(message))return message;
            }
            if(session.IsEnded)throw new InvalidOperationException(session.Status);
            await Task.Delay(10);
        }
        throw new TimeoutException("Network precision aim waiting for authoritative snapshots: "+session.Status);
    }
    private static long Tick(JsonElement frame)=>frame.GetProperty("k").GetInt64();
    private static JsonElement Data(JsonElement frame)=>frame.GetProperty("d");
    private static JsonElement Player(JsonElement frame,string id)=>Data(frame).GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==id);
    private static JsonElement? FindBomb(JsonElement frame,string id)
    {
        foreach(var bomb in Data(frame).GetProperty("bombs").EnumerateArray())if(bomb.GetProperty("ownerId").GetString()==id)return bomb;
        return null;
    }
    private static JsonElement Bomb(JsonElement frame,string id)=>FindBomb(frame,id)??throw new InvalidOperationException("Expected carried or thrown network bomb.");
    private static float F(JsonElement element,string key)=>element.GetProperty(key).GetSingle();
    private static void Check(bool condition,string description){if(!condition)throw new InvalidOperationException("FAIL "+description);}
}
