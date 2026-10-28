using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BlastCircuit.App;
using Bomber.Client.Peer;
using Bomber.Client.Sessions;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // Inspection stays inside QA; no native connection/IP details enter the product wire protocol.
    // Diagnostic leaf (in no release suite): terminally close one native WebRTC
    // connection mid-match, unlike FaultablePeerLink's silent packet suppression.
    // Relay continuity and direct recovery are reported as separate predicates.
    private async Task PeerLinkRecovery()
    {
        await _root.StartOnlineAsync(new Uri(_server),"Recovery host",new SessionSettings(Stage:"top-rules",Bots:0,Wins:1),NetworkMode.CreatePeer);
        var host=(NetworkSession)_root.ActiveSession!;
        await using var second=await NetworkSession.ConnectAsync(new Uri(_server),"Recovery second",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,
            peerFactory:()=>new GodotPeerLink(useStun:!new Uri(_server).IsLoopback));
        JsonElement? latest=null;
        using var live=KeepFixtureLive(second,m=>{if(m.GetProperty("t").GetString()=="state")latest=m.GetProperty("d").Clone();});
        async Task<bool> Until(Func<bool> predicate,double seconds)
        {
            var clock=Stopwatch.StartNew();ulong startFrame=Godot.Engine.GetProcessFrames();
            while(!predicate()&&Waiting(clock,seconds,startFrame))await Frames(1);
            return predicate();
        }
        void Record(string stage)=>File.WriteAllText(Path.Combine(_output,"peer-link-recovery.json"),JsonSerializer.Serialize(new{stage,
            host=PeerDiagnostics(host),second=PeerDiagnostics(second),cadence=live.Cadence},new JsonSerializerOptions{WriteIndented=true}));
        try
        {
            Check(await Until(()=>host.Party?.CanStart==true&&second.Party?.CanStart==true,20),"two compatible peers join");
            host.RequestStart();
            Check(await Until(()=>host.PeerTransport=="Direct P2P"&&second.PeerTransport=="Direct P2P"&&latest is not null,30),"direct link carries authoritative state before the close");
            var native=NativePeer(second,host.PlayerId!);
            Check(native is not null,"second peer exposes its native link to the host");
            int epoch=second.Party!.Epoch;
            string injection=Guid.NewGuid().ToString("N");
            void Mark(string stage)
            {
                var marker=new{injection,stage,utcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),frame=Engine.GetProcessFrames(),epoch,
                    from=second.PlayerId,to=host.PlayerId};
                GD.Print("QA_NATIVE_LINK_FAULT "+JsonSerializer.Serialize(marker));Event("peer-native-fault-stage",marker);
            }
            Mark("begin");native!.Close();Mark("closed");
            Event("peer-native-close",new{from=second.PlayerId,to=host.PlayerId,epoch,generation=second.ConnectionGeneration,injection});
            Check(await Until(()=>second.PeerTransport!="Direct P2P",20),"terminal native close leaves direct transport");
            long before=latest!.Value.GetProperty("serverTime").GetInt64();
            Check(await Until(()=>latest!.Value.GetProperty("serverTime").GetInt64()>before+2000&&second.Party?.Epoch==epoch,30),
                "authoritative state continues over relay in the same match after a terminal close");
            bool recovered=await Until(()=>host.PeerTransport=="Direct P2P"&&second.PeerTransport=="Direct P2P",60);
            Mark(recovered?"recovered":"not-recovered");
            Record(recovered?"recovered":"not-recovered");
            Check(recovered,"direct transport recovers after a terminal native link close");
            await Capture("peer-link-recovered");
            Check(host.PeerTransport=="Direct P2P"&&second.PeerTransport=="Direct P2P"&&second.Party?.Epoch==epoch&&_root.ScreenName=="Match",
                "recovered direct links and the same match survive the actual capture");
            Record("recovered-after-capture");Mark("capture-complete");
        }
        catch(InvalidOperationException){if(!File.Exists(Path.Combine(_output,"peer-link-recovery.json")))Record("failed");throw;}
        finally{live.Dispose();_root.Back();await Frames(3);}
    }
    private static WebRtcPeerConnection? NativePeer(NetworkSession session,string remote)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var peer=typeof(NetworkSession).GetField("_peer",flags)?.GetValue(session);
        var links=(IDictionary?)peer?.GetType().GetField("_links",flags)?.GetValue(peer);
        if(links?[remote] is not {} link)return null;
        var native=link.GetType().GetField("_inner",flags)?.GetValue(link)??link;
        return native.GetType().GetField("_peer",flags)?.GetValue(native) as WebRtcPeerConnection;
    }
    private static object PeerDiagnostics(NetworkSession session)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var peer=typeof(NetworkSession).GetField("_peer",flags)?.GetValue(session);
        if(peer is null)return new {transport=session.PeerTransport,status=session.Status,player=session.PlayerId,generation=session.ConnectionGeneration,events=session.ConnectionEvents,links=Array.Empty<object>()};
        var links=(IDictionary?)peer.GetType().GetField("_links",flags)?.GetValue(peer);
        var seen=peer.GetType().GetField("_directSeen",flags)?.GetValue(peer) as Dictionary<string,long>;
        var result=new List<object>();
        if(links is not null)foreach(DictionaryEntry entry in links)
        {
            string id=(string)entry.Key;object link=entry.Value!;
            var native=link.GetType().GetField("_inner",flags)?.GetValue(link)??link;
            var connection=native.GetType().GetField("_peer",flags)?.GetValue(native) as WebRtcPeerConnection;
            var reliable=native.GetType().GetField("_channel",flags)?.GetValue(native) as WebRtcDataChannel;
            var snapshots=native.GetType().GetField("_states",flags)?.GetValue(native) as WebRtcDataChannel;
            result.Add(new{id,connected=((IPeerLink)link).Connected,
                seenSeconds=seen?.TryGetValue(id,out long timestamp)==true?(double?)Stopwatch.GetElapsedTime(timestamp).TotalSeconds:null,
                connection=connection?.GetConnectionState().ToString(),
                signaling=connection?.GetSignalingState().ToString(),reliable=reliable?.GetReadyState().ToString(),snapshots=snapshots?.GetReadyState().ToString()});
        }
        return new {transport=session.PeerTransport,status=session.Status,player=session.PlayerId,generation=session.ConnectionGeneration,events=session.ConnectionEvents,party=session.Party,links=result};
    }
}
