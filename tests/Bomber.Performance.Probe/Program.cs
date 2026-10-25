using Bomber.Game;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

if (args.FirstOrDefault() == "--peer-capacity") { await PeerLoadProbe.Run(args[1..]); return; }

if (args.FirstOrDefault() == "--capacity") { await CapacityProbe.Run(args[1..]); return; }

if(args.Length<2)throw new ArgumentException("Usage: <server URL> <output.json> [seconds=90] [humans=3] [bots=3]");
var origin=new Uri(args[0]);string output=args[1];int seconds=args.Length>2?int.Parse(args[2]):90,humans=args.Length>3?int.Parse(args[3]):3,bots=args.Length>4?int.Parse(args[4]):3;
if(seconds<10||seconds>600||humans<1||humans+bots>8||bots<0)throw new ArgumentException("Invalid probe size/duration.");
using var http=new HttpClient{BaseAddress=origin,Timeout=TimeSpan.FromSeconds(15)};
var peers=new List<Peer>();var start=Stopwatch.StartNew();string? lobbyId=null;
try
{
    for(int i=0;i<humans;i++)
    {
        var guest=await Api("guests",new{displayName="Perf "+i});peers.Add(new Peer(guest.GetProperty("token").GetString()!,i));
    }
    var settings=new Dictionary<string,string>{{"party","on"},{"hosting","server"},{"stage","sunken-temple"},{"bots",bots.ToString()},{"wins","5"},{"round-seconds","120"},{"bot-difficulty","normal"},{"ghosts","on"},{"sudden-death","on"}};
    var created=await Api("lobbies",new{game="blast-room",visibility="private",settings},peers[0].Token);
    lobbyId=created.GetProperty("lobby").GetProperty("lobbyId").GetString();string code=created.GetProperty("lobby").GetProperty("joinCode").GetString()!;
    await peers[0].Connect(origin,created.GetProperty("reservation"));
    for(int i=1;i<humans;i++)await peers[i].Connect(origin,await Api("lobbies/join",new{code},peers[i].Token));
    foreach(var peer in peers)peer.StartInputs();
    peers[0].AllowStart=true;
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    foreach(var peer in peers)await peer.Stop();
    if(peers.Any(p=>p.Snapshots.Count<seconds*5||p.Error!=null||p.LastHumans!=humans||p.LastBots!=bots))throw new Exception("Probe did not receive sustained gameplay: "+string.Join(";",peers.Select(p=>p.Error?.Message)));
    var result=new{schemaVersion=1,origin=origin.ToString(),seconds,humans,bots,lobbyId,clients=peers.Select(p=>p.Summary(seconds)).ToArray()};
    var path=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("PASS performance probe: "+humans+" humans + "+bots+" bots; "+seconds+"s; "+path);
}
finally
{
    foreach(var peer in peers)
    {
        await peer.Stop();
        if(lobbyId!=null)try{await Api("lobbies/"+lobbyId+"/leave",new{},peer.Token);}catch(HttpRequestException){}
    }
}
async Task<JsonElement> Api(string path,object body,string? token=null)
{
    using var request=new HttpRequestMessage(HttpMethod.Post,"/realtime/v1/"+path){Content=JsonContent.Create(body)};
    request.Headers.Add(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
    if(token!=null)request.Headers.Add("x-player-token",token);
    using var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return default;return await response.Content.ReadFromJsonAsync<JsonElement>();
}
sealed class Peer(string token,int seat)
{
    public string Token=>token;
    public List<(double At,long Tick,int Bytes,int Events,int EventBytes,int Match,long ServerAt)> Snapshots{get;}=[];
    public List<double> Pings{get;}=[];
    public Exception? Error{get;private set;}
    public volatile bool AllowStart;
    private readonly CancellationTokenSource _stop=new();
    private readonly ClientWebSocket _socket=new();
    private Task? _reader,_writer;private volatile bool _canStart;private int _matchNumber,_epoch;
    public int LastHumans {get;private set;}
    public int LastBots {get;private set;}
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    public async Task Connect(Uri origin,JsonElement reservation)
    {
        var url=new UriBuilder(new Uri(origin,reservation.GetProperty("connectUrl").GetString()!));url.Scheme=url.Scheme=="https"?"wss":"ws";
        _socket.Options.SetRequestHeader(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
        await _socket.ConnectAsync(url.Uri,_stop.Token);await Send(new{t="peer.hello",d=new{version=OnlineProtocol.Revision}});_reader=Read();
    }
    public void StartInputs()=>_writer=Write();
    private async Task Write()
    {
        long seq=0;using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1d/30));
        try
        {
            while(await timer.WaitForNextTickAsync(_stop.Token))
            {
                if(AllowStart&&_canStart){_canStart=false;await Send(new{t="party.start",d=new{}});}
                seq++;int direction=(int)(_clock.Elapsed.TotalSeconds/1.5+seat)%4;
                int x=direction==0?1:direction==2?-1:0,z=direction==1?1:direction==3?-1:0;
                await Send(new{t="input",s=seq,d=new{x,z,a=seq%50==0,b=false,r=false,aim=false}});
                if(seq%30==0)await Send(new{t="peer.heartbeat",d=new{epoch=_epoch}});
                if(seq%15==0)await Send(new{t="sys.ping",d=new{sent=Stopwatch.GetTimestamp()}});
            }
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
        catch(Exception e){Error=e;}
    }
    private Task Send(object data)=>_socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(data)),WebSocketMessageType.Text,true,_stop.Token);
    private async Task Read()
    {
        var buffer=new byte[65536];
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                using var bytes=new MemoryStream();WebSocketReceiveResult response;
                do{response=await _socket.ReceiveAsync(buffer,_stop.Token);if(response.MessageType==WebSocketMessageType.Close)throw new IOException("Unexpected socket close.");bytes.Write(buffer,0,response.Count);}while(!response.EndOfMessage);
                using var doc=JsonDocument.Parse(bytes.GetBuffer().AsMemory(0,(int)bytes.Length));var root=doc.RootElement;string? type=root.GetProperty("t").GetString();var data=root.GetProperty("d");
                if(type=="state")
                {
                    var events=data.GetProperty("events");Snapshots.Add((_clock.Elapsed.TotalMilliseconds,root.GetProperty("k").GetInt64(),(int)bytes.Length,events.GetArrayLength(),System.Text.Encoding.UTF8.GetByteCount(events.GetRawText()),_matchNumber,data.TryGetProperty("serverTime",out var serverTime)?serverTime.GetInt64():0));
                    LastHumans=LastBots=0;foreach(var player in data.GetProperty("players").EnumerateArray()){if(player.GetProperty("bot").GetBoolean())LastBots++;else LastHumans++;}
                }
                else if(type=="party.status"){_epoch=data.TryGetProperty("epoch",out var epoch)?epoch.GetInt32():0;_canStart=data.GetProperty("phase").GetString()=="lobby"&&data.GetProperty("canStart").GetBoolean();_matchNumber=data.GetProperty("match").GetInt32();}
                else if(type=="sys.pong")Pings.Add(Stopwatch.GetElapsedTime(data.GetProperty("echo").GetProperty("sent").GetInt64()).TotalMilliseconds);
                else if(type=="sys.error")throw new IOException("Server rejected probe: "+data.GetProperty("code").GetString());
            }
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested){}
        catch(WebSocketException)when(_stop.IsCancellationRequested){}
        catch(Exception e){Error=e;}
    }
    public async Task Stop()
    {
        _stop.Cancel();_socket.Abort();
        if(_reader!=null)await _reader;if(_writer!=null)await _writer;
    }
    public object Summary(int seconds)
    {
        var steady=Snapshots.Where(s=>s.At>=10000).ToArray();var pairs=steady.Zip(steady.Skip(1)).Where(pair=>pair.First.Match==pair.Second.Match).ToArray();var gaps=pairs.Select(pair=>pair.Second.At-pair.First.At).ToArray();
        var timed=pairs.Where(pair=>pair.First.ServerAt>0&&pair.Second.ServerAt>0).ToArray();
        return new{seat,humans=LastHumans,bots=LastBots,snapshots=Snapshots.Count,steadySnapshots=steady.Length,bytesPerSecond=Snapshots.Sum(s=>(long)s.Bytes)/(double)seconds,
            snapshotBytes=Stats(steady.Select(s=>(double)s.Bytes)),eventBytes=Stats(steady.Select(s=>(double)s.EventBytes)),events=Stats(steady.Select(s=>(double)s.Events)),
            arrivalGapMs=Stats(gaps),serverGenerationGapMs=Stats(timed.Select(pair=>(double)(pair.Second.ServerAt-pair.First.ServerAt))),deliveryJitterMs=Stats(timed.Select(pair=>Math.Max(0,pair.Second.At-pair.First.At-(pair.Second.ServerAt-pair.First.ServerAt)))),gapsOver150Ms=gaps.Count(g=>g>150),gapsOver250Ms=gaps.Count(g=>g>250),pingMs=Stats(Pings),
            timeline=steady.Select(s=>new{s.At,s.Tick,s.Bytes,s.Events,s.Match,s.ServerAt}).ToArray()};
    }
    private static object Stats(IEnumerable<double> values)
    {
        var sorted=values.Order().ToArray();double Q(double q)=>sorted.Length==0?0:sorted[(int)Math.Ceiling((sorted.Length-1)*q)];
        return new{count=sorted.Length,mean=sorted.Length==0?0:sorted.Average(),p50=Q(.5),p95=Q(.95),p99=Q(.99),max=Q(1)};
    }
}
