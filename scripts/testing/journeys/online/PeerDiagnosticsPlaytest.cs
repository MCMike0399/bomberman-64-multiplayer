using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Bomber.Client.Peer;
using Bomber.Client.Sessions;
using Godot;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    // Inspection stays inside QA; no native connection/IP details enter the product wire protocol.
    private static object PeerDiagnostics(NetworkSession session)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var peer=typeof(NetworkSession).GetField("_peer",flags)?.GetValue(session);
        if(peer is null)return new {transport=session.PeerTransport,links=Array.Empty<object>()};
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
        return new {transport=session.PeerTransport,party=session.Party,links=result};
    }
}
