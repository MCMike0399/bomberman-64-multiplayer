using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;
using Godot;

namespace BlastCircuit.Testing;

/// <summary>Opt-in multi-process/multi-machine probe; each process uses its own real product session.</summary>
public partial class PlaytestDriver
{
    private async Task PeerExternal()
    {
        var args=OS.GetCmdlineUserArgs();
        string code=args.FirstOrDefault(a=>a.StartsWith("--peer-code="))?.Split('=',2)[1]??"";
        bool creator=code.Length==0;
        await _root.StartOnlineAsync(new Uri(_server),creator?"Cross-host owner":"Cross-host peer",new SessionSettings(Bots:0,Wins:5),creator?NetworkMode.CreatePeer:NetworkMode.JoinCode,code);
        var session=(NetworkSession)_root.ActiveSession!;
        var receipt=Path.Combine(_output,"peer-external.json");
        void Save(bool passed)=>File.WriteAllText(receipt,JsonSerializer.Serialize(new
        {passed,creator,code=session.JoinCode,session.LobbyId,session.PlayerId,transport=session.PeerTransport,
            route=session.ConnectionRoute,sent=session.DirectPacketsSent,received=session.DirectPacketsReceived,epoch=session.Party?.Epoch,match=session.Party?.Match,snapshots=_root.SnapshotCount}));
        Save(false);
        var clock=Stopwatch.StartNew();double playingAt=-1,completedAt=-1;int firstEpoch=0;
        long migrationSent=-1,migrationReceived=-1;
        while(clock.Elapsed.TotalSeconds<90)
        {
            if(session.IsEnded)throw new InvalidOperationException(session.Status);
            if(completedAt>=0&&clock.Elapsed.TotalSeconds-completedAt>5)return;
            if(!creator&&firstEpoch>0&&session.Party?.Match>=2&&migrationSent<0)
            {migrationSent=session.DirectPacketsSent;migrationReceived=session.DirectPacketsReceived;}
            if(!creator&&migrationSent>=0&&completedAt<0&&session.DirectPacketsSent-migrationSent>20&&session.DirectPacketsReceived-migrationReceived>20)
            {
                Check(true,"external peer exchanges new direct gameplay after host takeover");
                completedAt=clock.Elapsed.TotalSeconds;Save(true);
            }
            if(session.IsLeader&&session.Party?.CanStart==true&&session.Party.Phase=="lobby"&&
                ((!creator&&session.Party.Match>0)||(_root.LatestLobby is {} lobby&&LobbyStatus.Parse(lobby).Players.Count(p=>p.Connected)>=3)))session.RequestStart();
            if(session.Party?.Phase=="playing"&&_root.LatestState is not null)
            {
                if(playingAt<0){playingAt=clock.Elapsed.TotalSeconds;firstEpoch=session.Party.Epoch;}
                int beat=(int)((clock.Elapsed.TotalSeconds-playingAt)*4);
                Input(0,new PlayerInput(beat%8<4?1:-1,0,A:beat%12==0));
                if(completedAt<0)Save(false);
                if(creator&&clock.Elapsed.TotalSeconds-playingAt>12)
                {
                    Check(session.DirectPacketsSent>20&&session.DirectPacketsReceived>20,"external host exchanges real direct gameplay packets");
                    Save(true);_root.Back();return;
                }
            }
            await Frames(1);await Task.Delay(16);
        }
        Save(false);Check(false,"external peer scenario completed before timeout");
    }
}
