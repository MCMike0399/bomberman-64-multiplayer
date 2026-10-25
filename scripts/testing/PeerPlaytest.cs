using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.App;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task PeerRooms()
    {
        _root.Navigate("Online"); await Frames(3); await Capture("peer-online-options");
        Check(_root.FindChildren("*","",true,false).OfType<Godot.Button>().Any(b=>b.IsVisibleInTree()&&b.Text=="Host peer room") && _root.FindChildren("*","",true,false).OfType<Godot.Button>().Any(b=>b.IsVisibleInTree()&&b.Text=="Random lobby"),"online screen offers peer hosting and random lobbies");
        await _root.StartOnlineAsync(new Uri(_server),"Peer host",new SessionSettings(Bots:0,Wins:1),NetworkMode.CreatePeer);
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
        second.SendInput(0,new PlayerInput(0,1,A:true));
        await Until(()=>_root.LatestState is {} s&&s.GetProperty("bombs").GetArrayLength()>0&&states.TryGetValue(third.PlayerId!,out var remote)&&remote.GetProperty("bombs").GetArrayLength()>0,"remote direct input creates a bomb visible to other peers");
        second.ReleaseInputs(); await Capture("peer-direct-three-players");
        Check(host.DirectPacketsReceived>0&&second.DirectPacketsSent>0&&third.DirectPacketsReceived>0,"gameplay packets traversed real direct channels, not only signalling");
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
            Check(predicate(),description);
        }
    }

    private async Task PeerFullRoom()
    {
        await _root.StartOnlineAsync(new Uri(_server),"Eight-peer host",new SessionSettings(Bots:0),NetworkMode.CreatePeer);
        var host=(NetworkSession)_root.ActiveSession!;
        var guests=new List<NetworkSession>();var states=new Dictionary<string,JsonElement>();
        try
        {
            for(int i=1;i<8;i++)guests.Add(await NetworkSession.ConnectAsync(new Uri(_server),$"Peer {i+1}",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,peerFactory:()=>new GodotPeerLink(useStun:!new Uri(_server).IsLoopback)));
            await Until(()=>host.Party?.CanStart==true&&guests.All(p=>p.Party?.CanStart==true),"eight compatible peers can start");
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
                foreach(var peer in guests)
                {
                    peer.Advance(1d/30);
                    foreach(var m in peer.DrainMessages())if(m.GetProperty("t").GetString()=="state")states[peer.PlayerId!]=m.GetProperty("d").Clone();
                }
                await Frames(1);await Task.Delay(16);
            }
            if(!predicate())Event("peer-capacity-failure",new{description,host=host.PeerTransport,guests=guests.Select(p=>new{p.PlayerId,p.PeerTransport,p.DirectPacketsSent,p.DirectPacketsReceived,phase=p.Party?.Phase,epoch=p.Party?.Epoch,state=states.TryGetValue(p.PlayerId!,out var state)?state.GetProperty("players").GetArrayLength():0})});
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
        public void Dispose()=>_inner.Dispose();
    }
}
