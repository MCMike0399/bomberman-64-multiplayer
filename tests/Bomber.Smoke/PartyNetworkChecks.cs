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
        await AdvancedRoom(origin);
        await EightPlayerRoom(origin);
        await KickStopAndNames(origin);
        Console.WriteLine("PASS persistent private party: manual leader start, real match end, same-code lobby return, rematch and leader transfer");
    }
    private static async Task AdvancedRoom(Uri origin)
    {
        var settings=new SessionSettings(Bots:0,SuddenDeath:false);
        await using var host=await NetworkSession.ConnectAsync(origin,"Rules leader",settings);
        await using var peer=await NetworkSession.ConnectAsync(origin,"Rules guest",settings,NetworkMode.JoinCode,host.JoinCode);
        await Until(()=>host.Party?.CanStart==true&&peer.Party!=null,"advanced room ready");
        var custom=settings with{Advanced=new AdvancedRules{DropPercent=0,BlockPercent=0,MaxFire=4,MaxBombs=2,ItemSeconds=5,EffectSeconds=5,DisabledItems=string.Join(',',AdvancedRules.ItemTypes)}};
        host.SetRoomSettings(custom);
        await Until(()=>peer.Party?.Settings is {} rules&&SessionSettings.FromWire(rules).Advanced==custom.Advanced,"peer receives full advanced rules edit");
        await Start(host);
        var snapshot=await State(peer,s=>s.GetProperty("phase").GetInt32()==1);
        if(Convert.FromBase64String(snapshot.GetProperty("softBlocks").GetString()!).Any(b=>b!=0)||snapshot.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("bombs").GetInt32()!=2||p.GetProperty("maxFire").GetInt32()!=4))throw new Exception("Online authority ignored advanced settings.");
        Console.WriteLine("PASS real network advanced rules edit reaches peer and controls cover, starting inventory and caps");
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
    private static async Task KickStopAndNames(Uri origin)
    {
        var settings=new SessionSettings(Stage:"sunken-temple",Bots:0,SuddenDeath:false);
        await using var host=await NetworkSession.ConnectAsync(origin,"",settings);
        await using var peer=await NetworkSession.ConnectAsync(origin,"Mika",settings,NetworkMode.JoinCode,host.JoinCode);
        await Start(host);
        var initial=await State(host,s=>s.GetProperty("phase").GetInt32()==1);
        var players=initial.GetProperty("players").EnumerateArray().ToArray();
        if(players.Single(p=>p.GetProperty("id").GetString()==host.PlayerId).GetProperty("name").GetString()!="P1"||
            players.Single(p=>p.GetProperty("id").GetString()==peer.PlayerId).GetProperty("name").GetString()!="Mika")
            throw new Exception("Online chosen/blank names did not reach authority snapshots.");
        // Use the temple's permanently clear horizontal row, not a random cover cell
        // a fast kicked bomb can reach before a network round trip completes.
        host.SendInput(0,new PlayerInput(1,0));
        await State(host,s=>s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("fx").GetSingle()>.9f));
        host.SendInput(0,default);
        host.SendInput(0,new PlayerInput(0,0,A:true));host.SendInput(0,default);
        await State(host,s=>s.GetProperty("bombs").GetArrayLength()==1);await Task.Delay(100);
        host.SendInput(0,new PlayerInput(0,0,A:true));host.SendInput(0,default);
        await State(host,s=>s.GetProperty("bombs").GetArrayLength()==1&&s.GetProperty("bombs")[0].GetProperty("state").GetInt32()==1);
        host.SendInput(0,new PlayerInput(0,0,Stop:true));host.SendInput(0,default);
        var stopped=await State(peer,s=>s.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="kick-stop"));
        var bomb=stopped.GetProperty("bombs")[0];float x=bomb.GetProperty("x").GetSingle(),z=bomb.GetProperty("z").GetSingle();
        if(bomb.GetProperty("state").GetInt32()!=0)throw new Exception("Queued network stop tap did not stop the kicked bomb.");
        await Task.Delay(150);
        var later=await State(host,s=>s.GetProperty("bombs").GetArrayLength()==1&&s.GetProperty("bombs")[0].GetProperty("state").GetInt32()==0);
        if(later.GetProperty("bombs")[0].GetProperty("z").GetSingle()!=z||later.GetProperty("bombs")[0].GetProperty("x").GetSingle()!=x)throw new Exception("Stopped online bomb kept drifting.");
        Console.WriteLine("PASS real network queued kick-stop tap, peer observation, chosen name and unnamed seat fallback");
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
