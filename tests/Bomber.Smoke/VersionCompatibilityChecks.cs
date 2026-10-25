using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

internal static class VersionCompatibilityChecks
{
    public static async Task Run(Uri origin)
    {
        using var http=new HttpClient {BaseAddress=origin,Timeout=TimeSpan.FromSeconds(10)};
        int checks=0;
        void Check(bool ok,string description){if(!ok)throw new Exception(description);checks++;}
        async Task<HttpResponseMessage> Request(HttpMethod method,string path,string[]? protocol,object? body=null,string? token=null)
        {
            using var request=new HttpRequestMessage(method,path);
            if(protocol is not null)request.Headers.TryAddWithoutValidation(OnlineProtocol.HeaderName,protocol);
            if(token is not null)request.Headers.Add("x-player-token",token);
            if(body is not null)request.Content=JsonContent.Create(body);
            return await http.SendAsync(request);
        }
        foreach(var protocol in new string[]?[]{null,["1"],["99"],["bad"],["2","2"]})
        foreach(var (method,path) in new[]{(HttpMethod.Post,"/realtime/v1/guests"),(HttpMethod.Post,"/REALTIME/v1/GUESTS/"),
            (HttpMethod.Post,"/realtime/v1/lobbies"),(HttpMethod.Post,"/realtime/v1/lobbies/join"),(HttpMethod.Post,"/realtime/v1/lobbies/example/join"),
            (HttpMethod.Post,"/realtime/v1/queue"),(HttpMethod.Get,"/realtime/v1/queue/example")})
        {
            using var response=await Request(method,path,protocol,new{displayName="Legacy protocol probe"});
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(response.StatusCode==HttpStatusCode.Conflict&&json.RootElement.GetProperty("error").GetProperty("message").GetString()==OnlineProtocol.UpdateMessage,"legacy/malformed admission must receive the actionable update message: "+path);
        }
        using(var health=await Request(HttpMethod.Get,"/realtime/v1/games",null))Check(health.IsSuccessStatusCode,"games readiness remains ungated");
        using var guestResponse=await Request(HttpMethod.Post,"/realtime/v1/guests",[OnlineProtocol.WireRevision],new{displayName="Protocol verification"});
        Check(guestResponse.IsSuccessStatusCode&&guestResponse.Headers.TryGetValues(OnlineProtocol.HeaderName,out var protocolReply)&&OnlineProtocol.Accepts(protocolReply),"current guest admission echoes supported protocol");
        using var guest=JsonDocument.Parse(await guestResponse.Content.ReadAsStringAsync());string token=guest.RootElement.GetProperty("token").GetString()!;
        string? roomId=null;
        try
        {
            using var create=await Request(HttpMethod.Post,"/realtime/v1/lobbies",[OnlineProtocol.WireRevision],new{game="blast",visibility="private",settings=new{party="on",bots="0"}},token);
            Check(create.IsSuccessStatusCode,"current classic room creation succeeds");
            using var room=JsonDocument.Parse(await create.Content.ReadAsStringAsync());roomId=room.RootElement.GetProperty("lobby").GetProperty("lobbyId").GetString();
            var socketUri=NetworkSession.ResolveConnectUri(origin,room.RootElement.GetProperty("reservation").GetProperty("connectUrl").GetString()!);
            using var legacySocket=new ClientWebSocket();legacySocket.Options.CollectHttpResponseDetails=true;
            bool rejected=false;try{await legacySocket.ConnectAsync(socketUri,CancellationToken.None);}catch(WebSocketException){rejected=true;}
            Check(rejected&&legacySocket.HttpStatusCode==HttpStatusCode.Conflict,"worker rejects headerless socket before consuming reservation");
            using var socket=new ClientWebSocket();socket.Options.SetRequestHeader(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);socket.Options.CollectHttpResponseDetails=true;
            await socket.ConnectAsync(socketUri,CancellationToken.None);
            Check(socket.State==WebSocketState.Open&&socket.HttpResponseHeaders is not null&&OnlineProtocol.Accepts(socket.HttpResponseHeaders.Where(h=>h.Key.Equals(OnlineProtocol.HeaderName,StringComparison.OrdinalIgnoreCase)).SelectMany(h=>h.Value)),"same reservation succeeds with current protocol and worker response echo");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,"verification complete",new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }
        finally
        {
            if(roomId is not null){using var leave=await Request(HttpMethod.Post,$"/realtime/v1/lobbies/{roomId}/leave",null,new{},token);Check(leave.IsSuccessStatusCode,"authenticated leave remains ungated");}
        }
        using(var cancel=await Request(HttpMethod.Delete,"/realtime/v1/queue/missing",null,token:token))Check(cancel.StatusCode!=HttpStatusCode.Conflict,"cancellation is not intercepted by the version gate");
        Console.WriteLine($"PASS {checks} online version admission and worker handshake checks");
    }
}
