using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

internal static class PartyNetworkChecks
{
    public static async Task Start(NetworkSession host)
    {
        await Until(()=>host.Party?.CanStart==true,"leader can start");
        host.RequestStart();
    }
    public static async Task Run(Uri origin)
    {
        await using(var solo=await NetworkSession.ConnectAsync(origin,"Waiting host",new SessionSettings(Bots:1)))
        {
            await Until(()=>solo.Party is not null,"party status arrives");
            await Task.Delay(4200);
            if(solo.DrainMessages().Any(m=>m.GetProperty("t").GetString()=="state"))throw new Exception("Solo room auto-started with bots.");
        }
        var settings=new SessionSettings(Stage:"rock-garden",Bots:0,Wins:1,SuddenDeath:false);
        await using var host=await NetworkSession.ConnectAsync(origin,"Party leader",settings);
        await using var peer=await NetworkSession.ConnectAsync(origin,"Party guest",settings,NetworkMode.JoinCode,host.JoinCode);
        string? room=host.LobbyId,code=host.JoinCode;
        await Until(()=>peer.Party is not null,"guest party status arrives");
        peer.RequestStart();await Task.Delay(200);
        if(host.Party?.Phase!="lobby")throw new Exception("Guest started party.");
        await Start(host);
        await State(host,s=>s.GetProperty("phase").GetInt32()==1);
        host.SendInput(0,new PlayerInput(0,0,A:true));await Task.Delay(100);host.SendInput(0,default);
        await Until(()=>peer.Party?.Phase=="results","real match produces party results");
        if(host.IsEnded||peer.IsEnded)throw new Exception("Party transport ended with match.");
        await Until(()=>host.Party?.Phase=="lobby"&&peer.Party?.Phase=="lobby","both peers return to lobby");
        if(peer.LobbyId!=room||host.JoinCode!=code)throw new Exception("Rematch lost room identity.");
        host.DrainMessages();peer.DrainMessages();await Task.Delay(500);
        if(host.Party?.Phase!="lobby")throw new Exception("Party restarted automatically.");
        await Start(host);
        var state=await State(peer,s=>s.GetProperty("round").GetInt32()==1);
        if(state.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("wins").GetInt32()!=0))throw new Exception("Rematch kept old scores.");
        await host.DisposeAsync();await Until(()=>peer.IsLeader,"leader leave transfers room leadership");
        await EightPlayerRoom(origin);
        Console.WriteLine("PASS persistent private party: manual leader start, real match end, same-code lobby return, rematch and leader transfer");
    }
    private static async Task EightPlayerRoom(Uri origin)
    {
        var clients=new List<NetworkSession>();
        try
        {
            var host=await NetworkSession.ConnectAsync(origin,"Eight-player leader",new SessionSettings(Bots:0));clients.Add(host);
            for(int i=1;i<8;i++)clients.Add(await NetworkSession.ConnectAsync(origin,"Party player "+(i+1),new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode));
            if(clients.Any(c=>c.LobbyId!=host.LobbyId))throw new Exception("Eight-player party split rooms.");
            await Task.Delay(200);
            if(host.Party?.Phase!="lobby")throw new Exception("Full party started without its leader.");
            await Start(host);
            await Task.WhenAll(clients.Select(client=>State(client,s=>s.GetProperty("players").GetArrayLength()==8)));
            Console.WriteLine("PASS eight production party clients wait for leader and enter the same eight-human match");
        }
        finally {foreach(var client in clients)await client.DisposeAsync();}
    }
    private static async Task Until(Func<bool> predicate,string label)
    {
        var clock=Stopwatch.StartNew();while(clock.Elapsed<TimeSpan.FromSeconds(20))
        {if(predicate())return;await Task.Delay(20);}throw new TimeoutException(label);
    }
    private static async Task<JsonElement> State(NetworkSession session,Func<JsonElement,bool> predicate)
    {
        JsonElement? found=null;
        await Until(()=>{foreach(var message in session.DrainMessages())if(message.GetProperty("t").GetString()=="state"&&predicate(message.GetProperty("d")))found=message.GetProperty("d").Clone();return found!=null;},"party state");
        return found!.Value;
    }
}
