using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;

internal static class UpAndDownNetworkChecks
{
    public static async Task Run(Uri origin)
    {
        await RunMode(origin,NetworkMode.CreatePrivate);
        await RunMode(origin,NetworkMode.CreatePeer);
    }
    private static async Task RunMode(Uri origin,NetworkMode mode)
    {
        var settings=new SessionSettings(Stage:BlastStages.UpAndDown,Bots:0,SuddenDeath:false)
            {Advanced=new AdvancedRules {BlockPercent=0}};
        await using var host=await NetworkSession.ConnectAsync(origin,"Up & Down host",settings,mode);
        await using var peer=await NetworkSession.ConnectAsync(origin,"Up & Down observer",settings,NetworkMode.JoinCode,host.JoinCode);
        await PartyNetworkChecks.Start(host);
        JsonElement? stage=null,state=null;
        async Task Pump(Func<bool> ready)
        {
            var clock=Stopwatch.StartNew();
            while(clock.Elapsed.TotalSeconds<20)
            {
                host.Advance(.015);host.DrainMessages();peer.Advance(.015);
                foreach(var message in peer.DrainMessages())
                {
                    string? kind=message.GetProperty("t").GetString();
                    if(kind=="stage")stage=message.GetProperty("d").Clone();
                    if(kind=="state")state=message.GetProperty("d").Clone();
                }
                if(ready())return;
                if(peer.IsEnded)throw new Exception(peer.Status);
                await Task.Delay(15);
            }
            throw new TimeoutException("Up & Down network state did not arrive.");
        }
        await Pump(()=>stage is not null&&state is {} s&&s.GetProperty("phase").GetInt32()==1);
        var data=stage!.Value;int width=data.GetProperty("width").GetInt32();
        Check(data.GetProperty("cells")[21*width+18].GetProperty("below").GetProperty("t").GetInt32()==2,"live network stage carries the middle-pier underpass");
        Check(data.GetProperty("cells")[12*width+3].GetProperty("t").GetInt32()==2&&
            data.GetProperty("cells")[12*width+20].GetProperty("t").GetInt32()==2,"live network stage carries both side-drop landing edges");
        JsonElement Player()=>state!.Value.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
        async Task Go(float x,float z,float y)
        {
            await Pump(()=>{
                var p=Player();float dx=x-p.GetProperty("x").GetSingle(),dz=z-p.GetProperty("z").GetSingle();
                host.SendInput(0,new PlayerInput(Math.Clamp(dx*3,-1,1),Math.Clamp(dz*3,-1,1)));
                return Math.Abs(dx)<.13f&&Math.Abs(dz)<.13f;
            });
            host.SendInput(0,default);
            Check(Math.Abs(Player().GetProperty("y").GetSingle()-y)<.04f,$"live peer observes route {x},{z} at height {y}");
        }
        await Go(2.5f,12.5f,4);
        host.SendInput(0,new PlayerInput(1,0));
        await Pump(()=>Player().GetProperty("falling").GetBoolean());
        await Pump(()=>!Player().GetProperty("airborne").GetBoolean());
        host.SendInput(0,default);
        Check(Player().GetProperty("alive").GetBoolean()&&Math.Abs(Player().GetProperty("y").GetSingle()-2)<.04f,
            "live peer observes an ordinary inward side drop land on the middle pier");
        await Go(5.5f,12.5f,2);await Go(5.5f,7.5f,2);
        await Go(11.5f,7.5f,4);await Go(18.5f,7.5f,2);
        await Go(18.5f,22.5f,2);await Go(18.5f,21.5f,2);
        host.SendInput(0,new PlayerInput(0,0,A:true));
        await Pump(()=>state!.Value.GetProperty("bombs").GetArrayLength()>0);
        host.SendInput(0,default);
        Check(state!.Value.GetProperty("bombs")[0].GetProperty("y").GetSingle()==2,"network placement under the ring stays on the pier");
        Console.WriteLine($"PASS {mode} Up & Down layered stage transport, real route traversal and below-ring bomb placement");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
