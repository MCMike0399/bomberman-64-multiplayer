using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

internal static class PeerNetworkChecks
{
    public static async Task Run(Uri origin)
    {
        await RunStage(origin,"rock-garden");
        await RunStage(origin,"top-rules");
    }
    private static async Task RunStage(Uri origin,string stage)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var settings = new SessionSettings(Stage:stage,Bots:0,Wins:1);
        await using var host = await NetworkSession.ConnectAsync(origin,"Peer host",settings,NetworkMode.CreatePeer,cancellationToken:deadline.Token);
        Check(host.IsLeader && host.Party?.CanStart == false,"peer host waits for a second human");
        await using var second = await NetworkSession.ConnectAsync(origin,"Peer second",settings,NetworkMode.JoinCode,host.JoinCode,cancellationToken:deadline.Token);
        await using var third = await NetworkSession.ConnectAsync(origin,"Peer third",settings,NetworkMode.JoinCode,host.JoinCode,cancellationToken:deadline.Token);
        var sessions = new List<NetworkSession>{host,second,third};
        var states = new Dictionary<string,JsonElement>();
        var results = new Dictionary<string,JsonElement>();
        await Until(()=>host.Party?.CanStart==true && third.Party is not null);
        second.RequestStart(); await Pump(200);
        Check(host.Party?.Phase=="lobby","peer guest cannot start");
        host.RequestStart();
        await Until(()=>sessions.All(s=>states.TryGetValue(s.PlayerId!,out var state)&&state.GetProperty("phase").GetInt32()==1));
        Check(sessions.All(s=>s.PeerTransport=="Relay"),"forced relay scenario is labeled honestly");
        var before = Position(states[host.PlayerId!],second.PlayerId!);
        second.SendInput(0,new PlayerInput(0,1,A:true));
        await Until(()=>Position(states[host.PlayerId!],second.PlayerId!)!=before && states[third.PlayerId!].GetProperty("bombs").GetArrayLength()>0);
        second.ReleaseInputs();
        Check(states[third.PlayerId!].GetProperty("players").GetArrayLength()==3,"host simulation and guest intent reach all peers");
        if(stage=="top-rules")
        {
            async Task Walk(float x,float z)
            {
                var clock=Stopwatch.StartNew();
                while(clock.Elapsed<TimeSpan.FromSeconds(8))
                {
                    var player=states[host.PlayerId!].GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
                    float dx=x-player.GetProperty("x").GetSingle(),dz=z-player.GetProperty("z").GetSingle();
                    if(Math.Abs(dx)<.18f&&Math.Abs(dz)<.18f){host.ReleaseInputs();return;}
                    host.SendInput(0,new PlayerInput(Math.Clamp(dx*3,-1,1),Math.Clamp(dz*3,-1,1)));
                    await Pump(20);
                }
                throw new Exception("Peer castle route did not reach "+x+","+z);
            }
            await Walk(5.5f,1.5f);await Walk(2.5f,1.5f);await Walk(2.5f,6.5f);await Walk(16.5f,6.5f);
            await Until(()=>states[third.PlayerId!].GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("x").GetSingle()>16&&p.GetProperty("y").GetSingle()==0));
            Check(true,"peer-hosted Top Rules descends fractional stairs and crosses below three bridges as observed by a guest");
        }
        int epoch=second.Party!.Epoch;
        sessions.Remove(host); await host.DisposeAsync();
        await Until(()=>second.IsLeader && second.Party?.Phase=="lobby" && third.Party?.Epoch>epoch);
        Check(second.Party!.Epoch>epoch && second.LobbyId==third.LobbyId,"host departure transfers same room and invalidates old match");
        states.Clear(); second.RequestStart();
        await Until(()=>sessions.All(s=>states.TryGetValue(s.PlayerId!,out var state)&&state.GetProperty("players").GetArrayLength()==2));
        Check(second.IsLeader && !third.IsLeader,"new host runs a fresh two-peer match");
        await Until(()=>states[second.PlayerId!].GetProperty("phase").GetInt32()==1);
        second.SendInput(0,new PlayerInput(0,0,A:true));await Pump(100);second.ReleaseInputs();
        await Until(()=>states[third.PlayerId!].GetProperty("phase").GetInt32()==3);
        Check(third.Party?.Phase=="playing"&&!results.ContainsKey(third.PlayerId!),"peer guest receives the final arena announcement before results");
        await Pump(1500);
        Check(third.Party?.Phase=="playing"&&states[third.PlayerId!].GetProperty("phase").GetInt32()==3&&!results.ContainsKey(third.PlayerId!),"peer finish remains visible before result delivery");
        await Until(()=>third.Party?.Phase=="results"&&results.ContainsKey(third.PlayerId!));
        Check(results[third.PlayerId!].GetProperty("winners").EnumerateArray().Any(p=>p.GetString()==third.PlayerId),"peer result comes from an actual bomb elimination");
        await Until(()=>third.Party?.Phase=="lobby"&&second.Party?.Phase=="lobby");
        second.RequestStart();await Until(()=>third.Party?.Phase=="playing");
        // Keep signalling alive but stop polling the new host's authority: cluster lease must expire.
        sessions.Remove(second);
        await Until(()=>third.IsLeader && third.Party?.Phase=="lobby");
        Check(third.Party!.Epoch>epoch,"stalled host lease transfers leadership without needing socket closure");
        Console.WriteLine("PASS "+stage+" peer relay gameplay, authenticated start, departure migration and stalled-host recovery");

        async Task Pump(int milliseconds)
        {
            var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<milliseconds)
            {
                foreach(var s in sessions)
                {
                    s.Advance(.02);
                    foreach(var m in s.DrainMessages())
                    {
                        if(m.GetProperty("t").GetString()=="state")states[s.PlayerId!]=m.GetProperty("d").Clone();
                        if(m.GetProperty("t").GetString()=="party.result")results[s.PlayerId!]=m.GetProperty("d").Clone();
                    }
                    if(s.IsEnded)throw new Exception(s.Status);
                }
                await Task.Delay(20,deadline.Token);
            }
        }
        async Task Until(Func<bool> condition){while(!condition())await Pump(20);}
        static float Position(JsonElement state,string id)=>state.GetProperty("players").EnumerateArray().First(p=>p.GetProperty("id").GetString()==id).GetProperty("z").GetSingle();
        static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL "+text);Console.WriteLine("PASS "+text);}
    }
}
