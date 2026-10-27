using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Peer;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.App;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PeerRooms()
    {
        _root.Navigate("Online"); await Frames(3); await Capture("peer-online-options");
        Check(_root.FindChildren("*","",true,false).OfType<Godot.Button>().Any(b=>b.IsVisibleInTree()&&b.Text=="Create private room") && _root.FindChildren("*","",true,false).OfType<Godot.Button>().Any(b=>b.IsVisibleInTree()&&b.Text=="Play online"),"online screen offers automatic private rooms and public matchmaking");
        await _root.StartOnlineAsync(new Uri(_server),"Peer host",new SessionSettings(Stage:"top-rules",Bots:0,Wins:1),NetworkMode.CreatePeer);
        var host=(NetworkSession)_root.ActiveSession!;
        Check(host.Party?.Hosting=="peer"&&!host.Party.CanStart,"peer room requires two connected people");
        await Capture("peer-waiting-for-player");
        bool dropDirect=false;
        await using var second=await NetworkSession.ConnectAsync(new Uri(_server),"Peer second",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,peerFactory:()=>new FaultablePeerLink(()=>dropDirect,!new Uri(_server).IsLoopback));
        await using var third=await NetworkSession.ConnectAsync(new Uri(_server),"Peer third",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,peerFactory:()=>new GodotPeerLink(useStun:!new Uri(_server).IsLoopback));
        var peers=new[]{second,third};
        var states=new Dictionary<string,JsonElement>();
        await Until(()=>host.Party?.CanStart==true&&peers.All(p=>p.Party?.CanStart==true)&&NamedButton("StartRoom").IsVisibleInTree()&&!NamedButton("StartRoom").Disabled,"three peers join lobby and the rendered start control is ready");
        await ActivateNamed("StartRoom");
        await Until(()=>host.PeerTransport=="Direct P2P"&&peers.All(p=>p.PeerTransport=="Direct P2P")&&_root.LatestState is {} s&&s.GetProperty("phase").GetInt32()==1,"actual encrypted WebRTC channels connect");
        Check(_root.ScreenName=="Match","peer authority enters rendered gameplay");
        Check(_root.LatestStage is {} castleStage&&castleStage.GetProperty("id").GetString()=="top-rules"&&castleStage.GetProperty("underpasses").GetArrayLength()>40,"peer-hosted castle geometry reaches the real composition root");
        second.SendInput(0,new PlayerInput(0,1,A:true));
        await Until(()=>_root.LatestState is {} s&&s.GetProperty("bombs").GetArrayLength()>0&&states.TryGetValue(third.PlayerId!,out var remote)&&remote.GetProperty("bombs").GetArrayLength()>0,"remote direct input creates a bomb visible to other peers");
        second.ReleaseInputs(); await Capture("peer-direct-three-players");
        Check(host.DirectPacketsReceived>0&&second.DirectPacketsSent>0&&third.DirectPacketsReceived>0,"gameplay packets traversed real direct channels, not only signalling");
        await Until(()=>host.PeerTransport=="Direct P2P"&&peers.All(p=>p.PeerTransport=="Direct P2P"),"all direct links are healthy immediately before injecting loss");
        dropDirect=true;
        await Until(()=>second.PeerTransport=="Relay","silent direct-link failure selects the relay within the health timeout");
        long beforeRelay=states[third.PlayerId!].GetProperty("presentationSeq").GetInt64();
        second.SendInput(0,new PlayerInput(0,-1,A:true));
        await Until(()=>states[third.PlayerId!].GetProperty("presentationSeq").GetInt64()>beforeRelay,"gameplay continues through relay after silent direct loss");
        second.ReleaseInputs();dropDirect=false;
        await Until(()=>second.PeerTransport=="Direct P2P","healthy direct transport resumes after fallback");
        int epoch=second.Party!.Epoch;
        _root.Back(); await host.DisposeAsync();
        await Until(()=>second.IsLeader&&third.Party?.Epoch>epoch&&second.Party?.Phase=="lobby","leaving host transfers room to oldest remaining peer");
        states.Clear();second.RequestStart();
        await Until(()=>peers.All(p=>p.PeerTransport=="Direct P2P"&&states.ContainsKey(p.PlayerId!)),"new host rebuilds direct channels and starts a fresh match");
        Check(second.LobbyId==third.LobbyId,"host takeover keeps the room identity");
        Event("peer-transport",new {second=second.PeerTransport,third=third.PeerTransport,epoch=second.Party!.Epoch});
        await Capture("peer-host-takeover-complete");
        await second.DisposeAsync();await third.DisposeAsync();
        await PeerFullRoom();

        async Task Until(Func<bool> predicate,string description)
        {
            var clock=Stopwatch.StartNew();
            while(!predicate()&&clock.Elapsed<TimeSpan.FromSeconds(20))
            {
                foreach(var peer in peers)
                {
                    peer.Advance(1d/30);
                    foreach(var message in peer.DrainMessages())
                        if(message.GetProperty("t").GetString()=="state")states[peer.PlayerId!]=message.GetProperty("d").Clone();
                }
                await Frames(1);await Task.Delay(16);
            }
            if(!predicate())File.WriteAllText(Path.Combine(_output,"peer-room-diagnostics.json"),JsonSerializer.Serialize(new{description,host=PeerDiagnostics(host),peers=peers.Select(PeerDiagnostics)},new JsonSerializerOptions{WriteIndented=true}));
            Check(predicate(),description);
        }
    }

    private async Task PeerFullRoom()
    {
        await _root.StartOnlineAsync(new Uri(_server),"Eight-peer host",new SessionSettings(Bots:0),NetworkMode.CreatePeer);
        var host=(NetworkSession)_root.ActiveSession!;
        var guests=new List<NetworkSession>();var states=new Dictionary<string,JsonElement>();
        var rosters=new Dictionary<string,int>();
        try
        {
            for(int i=1;i<8;i++)
            {
                var joining=NetworkSession.ConnectAsync(new Uri(_server),$"Peer {i+1}",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,peerFactory:()=>new GodotPeerLink(useStun:!new Uri(_server).IsLoopback));
                while(!joining.IsCompleted)
                {
                    host.Advance(1d/30);
                    foreach(var peer in guests)peer.Advance(1d/30);
                    await Frames(1);await Task.Delay(16);
                }
                guests.Add(await joining);await Frames(1);
            }
            // CanStart means the two-human minimum, not that every pending
            // socket has joined. Wait for all eight in each accepted roster.
            await Until(()=>host.IsLeader&&host.Party?.CanStart==true&&guests.All(p=>p.Party?.CanStart==true&&
                rosters.GetValueOrDefault(p.PlayerId!)==8&&p.Party.Epoch==host.Party.Epoch),"eight compatible peers converge on the complete roster before start");
            var authorityClock=Stopwatch.StartNew();host.RequestStart();
            await Until(()=>host.PeerTransport=="Direct P2P"&&guests.All(p=>p.PeerTransport=="Direct P2P"&&states.TryGetValue(p.PlayerId!,out var state)&&state.GetProperty("players").GetArrayLength()==8&&state.GetProperty("phase").GetInt32()==1),"eight direct peers receive complete authoritative fight snapshots");
            Check(_root.LatestState!.Value.GetProperty("serverTime").GetInt64()<=authorityClock.ElapsedMilliseconds+300,"peer authority cannot accelerate with headless/render frame speed");
            Input(0,new PlayerInput(0,0,A:true));guests[0].SendInput(0,new PlayerInput(0,0,A:true));
            await Until(()=>states.Values.All(s=>s.GetProperty("bombs").GetArrayLength()>=2),"host and remote bomb actions reach all eight direct players");
            Input(0,default);guests[0].ReleaseInputs();await Capture("peer-eight-players");
        }
        finally{foreach(var peer in guests)await peer.DisposeAsync();_root.Back();}
        async Task Until(Func<bool> predicate,string description)
        {
            var clock=Stopwatch.StartNew();
            while(!predicate()&&clock.Elapsed.TotalSeconds<20)
            {
                host.Advance(1d/30);
                foreach(var peer in guests)
                {
                    peer.Advance(1d/30);
                    foreach(var m in peer.DrainMessages())
                    {
                        string? type=m.GetProperty("t").GetString();
                        if(type=="state")states[peer.PlayerId!]=m.GetProperty("d").Clone();
                        if(type=="party.status")rosters[peer.PlayerId!]=m.GetProperty("d").GetProperty("players").EnumerateArray().Count(p=>p.GetProperty("connected").GetBoolean());
                    }
                    // These seven clients share the engine thread in QA. Give
                    // the real host a frame between native connection polls.
                    await Frames(1);
                }
                await Task.Delay(16);
            }
            if(!predicate())
            {
                var diagnostic=new{description,host=host.PeerTransport,hostStatus=host.Status,hostParty=host.Party,guests=guests.Select(p=>new{p.PlayerId,p.PeerTransport,p.DirectPacketsSent,p.DirectPacketsReceived,p.Status,notice=p.Party?.Notice,members=rosters.GetValueOrDefault(p.PlayerId!),phase=p.Party?.Phase,epoch=p.Party?.Epoch,state=states.TryGetValue(p.PlayerId!,out var state)?state.GetProperty("players").GetArrayLength():0,statePhase=states.TryGetValue(p.PlayerId!,out var phaseState)?phaseState.GetProperty("phase").GetInt32():-1,serverTime=states.TryGetValue(p.PlayerId!,out var timedState)?timedState.GetProperty("serverTime").GetInt64():-1})};
                Event("peer-capacity-failure",diagnostic);
                File.WriteAllText(Path.Combine(_output,"peer-capacity-diagnostics.json"),JsonSerializer.Serialize(new{room=diagnostic,host=PeerDiagnostics(host),guests=guests.Select(PeerDiagnostics)},new JsonSerializerOptions{WriteIndented=true}));
            }
            Check(predicate(),description);
        }
    }

    private sealed class FaultablePeerLink(Func<bool> drop, bool useStun) : IPeerLink
    {
        private readonly GodotPeerLink _inner=new(useStun);
        public bool Connected=>_inner.Connected;
        public event Action<JsonElement>? Signal;
        public event Action<byte[]>? Received;
        public void Start(bool offer)
        {_inner.Signal+=s=>Signal?.Invoke(s);_inner.Received+=p=>{if(!drop())Received?.Invoke(p);};_inner.Start(offer);}
        public void AcceptSignal(JsonElement signal)=>_inner.AcceptSignal(signal);
        public void Poll()=>_inner.Poll();
        public bool Send(byte[] packet)=>drop()||_inner.Send(packet);
        public bool SendState(byte[] packet)=>drop()||_inner.SendState(packet);
        public void Dispose()=>_inner.Dispose();
    }
}
