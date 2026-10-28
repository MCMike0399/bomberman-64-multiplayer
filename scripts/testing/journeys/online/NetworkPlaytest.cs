using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using System.Text.Json;
using BlastCircuit.Arena.Models;
using BlastCircuit.Audio;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.UI.Hud;
namespace BlastCircuit.Testing;

/// <summary>Real HTTP/WebSocket host and guest sessions against an independent server.</summary>
public partial class PlaytestDriver
{
    private async Task PostMatch()
    {
        await Results();
        var settings=new SessionSettings(Bots:0,Wins:1,Ghosts:false){Hosting="server"};
        await using var owner=await NetworkSession.ConnectAsync(new Uri(_server),"Map selection owner",settings,NetworkMode.CreatePrivate);
        // This fixture owner has no game loop. Keep it live like a client; a ~15
        // minute software-rendered match otherwise overflowed its control queue
        // and handed the room to the guest.
        using var liveOwner=KeepFixtureLive(owner);
        await _root.StartOnlineAsync(new Uri(_server),"Map selection guest",settings,NetworkMode.JoinCode,owner.JoinCode,appearance:5);
        var guest=(NetworkSession)_root.ActiveSession!;
        string? code=guest.JoinCode,lobby=guest.LobbyId;
        await Until(()=>owner.Party?.CanStart==true&&guest.Party?.CanStart==true,"online map fixture has two connected players");
        owner.RequestStart();
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"online map fixture reaches fight",900);
        Check(State.GetProperty("players").EnumerateArray().All(p=>p.GetProperty("bombs").GetInt32()==2&&p.GetProperty("fire").GetInt32()==1),"online players start with zero collected upgrades");
        await NetworkTap(guest,new PlayerInput(0,0,A:true));
        await Until(()=>_root.ScreenName=="Results","ordinary bomb completes the online match");
        await Capture("online-post-match-results");
        await Until(()=>_root.ScreenName=="Setup"&&guest.Party?.Phase=="lobby","online results automatically return to map selection");
        Check(ReferenceEquals(guest,_root.ActiveSession)&&guest.JoinCode==code&&guest.LobbyId==lobby,"map selection retains the same online room and connection");
        Check(NamedButton("StageSelector").Disabled&&NamedButton("LaunchMatch").Disabled&&NamedButton("AdvancedSettings").Disabled,"online guest cannot change rules or start the next battle");
        await Capture("online-guest-map-selection");
        owner.SetRoomSettings(settings with{Stage="rock-garden"});
        await Until(()=>guest.Party?.Settings?.GetValueOrDefault("stage")=="rock-garden","guest receives the owner's selected arena");
        var menu=_root.FindChildren("*","",true,false).OfType<BlastCircuit.UI.Menu.ArcadeMenu>().Single();
        // Party is published by the socket worker before GameRoot drains that
        // message on the next engine frame. Require the actual menu state too.
        await Until(()=>menu.OnlineSettings.Stage=="rock-garden","guest map preview follows accepted room settings");
        liveOwner.Dispose();await owner.DisposeAsync();
        await Until(()=>guest.IsLeader&&!NamedButton("StageSelector").Disabled,"room ownership transfer enables map selection for the new host");
        Check(NamedButton("LaunchMatch").Disabled,"host cannot start without another human or CPU");
        NamedButton("CPUrivals").GrabFocus();await KeyPress(Key.Right);
        await Until(()=>guest.Party?.CanStart==true&&!NamedButton("LaunchMatch").Disabled,"accepted CPU rival enables next battle");
        NamedButton("StageSelector").GrabFocus();await KeyPress(Key.Right);
        await Capture("online-host-map-selection");await ActivateNamed("LaunchMatch");
        await WaitState(_=>_root.ScreenName=="Match"&&_root.LatestStage?.GetProperty("id").GetString()=="up-and-down","host starts the next match on the chosen arena",900);
        Check(guest.JoinCode==code&&guest.LobbyId==lobby,"next online battle keeps its room code and identity");
        _root.Back();await Frames(3);

        async Task Until(Func<bool> predicate,string description)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();ulong startFrame=Godot.Engine.GetProcessFrames();
            while(!predicate()&&Waiting(clock,45,startFrame)){await Frames(1);await Task.Delay(16);}
            Check(predicate(),description);
        }
    }

    private async Task Network()
    {
        _root.Navigate("Online");_root.SetOnlineAppearance(3);await Frames(3);
        await ActivateNamed("OnlineAppearance");
        Check(_root.SelectedAppearance==4,"online suit button selects the host appearance");
        NamedButton("CreatePrivateRoom").GrabFocus();
        var cancelled=_root.StartOnlineAsync(new Uri(_server),"Cancelled navigation trial",new SessionSettings(),NetworkMode.CreatePrivate,appearance:_root.SelectedAppearance);
        Check(NamedButton("CancelConnect").HasFocus(),"connecting immediately focuses its Cancel action");
        CtlKey(Key.Escape,true);CtlKey(Key.Escape,false);await cancelled;await Frames(4);
        Check(_root.ScreenName=="Online"&&NamedButton("CreatePrivateRoom").HasFocus()&&!NamedButton("CreatePrivateRoom").Disabled&&!NamedButton("CancelConnect").Visible,
            "Esc cancels a real pending connection and restores its enabled opener without stale deferred focus");
        await _root.StartOnlineAsync(new Uri(_server),"Harness host",new SessionSettings(Stage:"sunken-temple",Bots:0),NetworkMode.CreatePrivate,appearance:_root.SelectedAppearance);
        Check(_root.ActiveSession is NetworkSession,"engine uses product network adapter");
        var host=(NetworkSession)_root.ActiveSession!;
        Check(!string.IsNullOrWhiteSpace(host.JoinCode),"network host exposes join code");
        await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Harness peer",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,appearance:6);
        Check(peer.LobbyId==host.LobbyId,"second product client joins same kernel lobby");
        // One owner keeps this fixture live for its whole lifetime, including the
        // long software-rendered lobby and later settings/Quick Play steps. The
        // movement check consumes the state snapshots it collects.
        var peerStates=new Queue<JsonElement>();bool collectPeerStates=false;
        using var livePeer=KeepFixtureLive(peer,message=>{if(collectPeerStates&&message.GetProperty("t").GetString()=="state")peerStates.Enqueue(message.GetProperty("d").Clone());});
        bool fullLobby=false;
        for(var i=0;i<180 && !fullLobby;i++)
        {
            await Frames(1);await Task.Delay(16);
            if(_root.LatestLobby is {} rawLobby)
            {
                var status=LobbyStatus.Parse(rawLobby);
                fullLobby=status.Players.Count==2 && status.Players.All(p=>p.Connected) && host.Party?.CanStart==true;
            }
        }
        Check(fullLobby,"engine lobby receives two connected humans and manual start authority");
        Event("lobby",_root.LatestLobby!.Value);
        Check(_root.ScreenName=="Lobby","connected humans remain in waiting party lobby");
        var lobbyText=VisibleText(_root);
        Event("lobby-ui",new {text=lobbyText});
        Check(lobbyText.Contains("Harness host",StringComparison.Ordinal),"lobby renders host name in visible UI");
        Check(lobbyText.Contains("Harness peer",StringComparison.Ordinal),"lobby renders peer name in visible UI");
        Check(lobbyText.Contains("You own this room",StringComparison.Ordinal),"lobby identifies room ownership and manual start");
        await Task.Delay(4200);await Frames(2);Check(_root.ScreenName=="Lobby"&&_root.LatestState==null,"full private lobby does not automatically start");
        NamedButton("StartRoom").GrabFocus();await KeyPress(Key.Right);
        Check(NamedButton("EditRoomSettings").HasFocus(),"Right moves across the real lobby action row to room settings");
        await KeyPress(Key.Right);Check(NamedButton("LobbySettings").HasFocus(),"Right reaches personal settings in the lobby row");
        await KeyPress(Key.Left);Check(NamedButton("EditRoomSettings").HasFocus(),"Left returns along the lobby action row");
        await KeyPress(Key.Up);Check(NamedButton("CopyRoomCode").HasFocus(),"Up leaves the lobby action row for Copy code");
        await ActivateNamed("CopyRoomCode");
        Check(DisplayServer.ClipboardGet()==host.JoinCode,"copy button writes the actual room code to clipboard");
        Check(NamedButton("CopyRoomCode").Text=="Copied!","copy button confirms success");
        await Capture("network-lobby");
        await ActivateNamed("LobbySettings");await KeyPress(Key.Escape);
        Check(_root.ScreenName=="Lobby"&&NamedButton("LobbySettings").HasFocus()&&ReferenceEquals(host,_root.ActiveSession),"lobby Settings Back restores focus without leaving the room");
        await ActivateNamed("EditRoomSettings");await KeyPress(Key.Escape);
        Check(_root.ScreenName=="Lobby"&&NamedButton("EditRoomSettings").HasFocus()&&ReferenceEquals(host,_root.ActiveSession),"Esc from room rules returns to its lobby opener");
        string roomIdentity=host.LobbyId!;
        await ActivateNamed("EditRoomSettings");
        Check(_root.ScreenName=="Setup"&&host.LobbyId==roomIdentity,"leader opens existing rules editor without leaving room");
        await ActivateNamed("AdvancedSettings");
        Check(_root.ScreenName=="Advanced"&&ReferenceEquals(host,_root.ActiveSession),"online advanced page retains the live room");
        await ActivateNamed("Itemdropchance");await ActivateNamed("Allow_slow");
        await Capture("network-advanced-rules");await ActivateNamed("AdvancedBack");
        Check(_root.ScreenName=="Setup"&&host.LobbyId==roomIdentity,"advanced Back preserves existing room identity");
        var rules=_root.FindChildren("*","",true,false).OfType<BlastCircuit.UI.ArcadeRuleDial>().First(d=>d.Caption=="Wins to victory");
        rules.GrabFocus();CtlKey(Key.Right,true);await Frames(1);CtlKey(Key.Right,false);await Frames(2);
        await Capture("network-room-rules");
        await ActivateNamed("LaunchMatch");
        for(int i=0;i<180&&host.Party?.Settings?.GetValueOrDefault("wins")!="3";i++){await Frames(1);await Task.Delay(16);}
        Check(host.Party?.Settings?.GetValueOrDefault("wins")=="3"&&_root.ScreenName=="Lobby"&&host.LobbyId==roomIdentity,"saved match rules are accepted by server in the same room");
        for(int i=0;i<180&&peer.Party?.Settings?.GetValueOrDefault("wins")!="3";i++){await Frames(1);await Task.Delay(16);}
        Check(peer.Party?.Settings?.GetValueOrDefault("wins")=="3","room guest receives the leader's saved rules");
        Check(peer.Party?.Settings?.GetValueOrDefault("drop-percent")=="45"&&peer.Party.Settings.GetValueOrDefault("disabled-items")=="slow","advanced room settings reach the peer");
        CtlButton(0,JoyButton.Start,true);await Frames(1);CtlButton(0,JoyButton.Start,false);await Frames(2);
        Check(NamedButton("StartRoom").HasFocus()&&_root.ScreenName=="Lobby","Start focuses Start game without starting it");
        await ActivateNamed("StartRoom");
        try{await WaitState(s=>s.GetProperty("phase").GetInt32()==1 && s.GetProperty("players").GetArrayLength()==2,"engine receives two-human authoritative fight",900);}
        catch(InvalidOperationException)
        {
            File.WriteAllText(Path.Combine(_output,"network-diagnostics.json"),JsonSerializer.Serialize(new{host=PeerDiagnostics(host),peer=PeerDiagnostics(peer),cadence=livePeer.Cadence,
                latest=_root.LatestState is {} latest?new{phase=latest.GetProperty("phase").GetInt32(),players=latest.GetProperty("players").GetArrayLength()}:null},new JsonSerializerOptions{WriteIndented=true}));
            throw;
        }
        await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("appearance").GetInt32()==4)&&s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==peer.PlayerId&&p.GetProperty("appearance").GetInt32()==6),"network authority reflects both chosen appearances",180);
        Check(State.GetProperty("hurryMode").GetString()=="water","network server supplies temple-specific water sudden death");
        Check(State.TryGetProperty("waterLevel",out var networkWater)&&networkWater.ValueKind==JsonValueKind.Number,"network server publishes authoritative water height");
        Check(State.TryGetProperty("presentationSeq",out _)&&State.TryGetProperty("events",out _),"network server publishes accepted presentation events");
        var serverLighting=State.GetProperty("stageLighting").GetString();
        Check(LightingArena.TimeOfDay==BlastCircuit.Arena.StageLighting.FromWire(serverLighting),"online arena applies server lighting");
        Check(_root.FindChildren("*","",true,false).OfType<BlastCircuit.Arena.Models.BomberModel>().Any(model=>model.DisplayName=="Harness host"&&model.IsVisibleInTree()),"network character label uses chosen player name");
        Check(_root.FindChildren("*","",true,false).OfType<BlastCircuit.UI.Hud.PlayerToken>().Any(t=>t.Tag=="Harness host"),"HUD uses the same chosen name");
        int bombCueBefore=_root.BattleAudio.Counts.GetValueOrDefault("bomb");
        var before=Player.GetProperty("z").GetSingle();
        // Walk while tapping A: a held A charges a throw once software frames
        // stretch the hold, so the bomb press is a bounded wall-clock edge.
        peerStates.Clear();collectPeerStates=true;
        await NetworkTap(host,new PlayerInput(0,-1,A:true),new PlayerInput(0,-1));
        bool observed=false;
        for(var i=0;i<150 && !observed;i++)
        {
            await Frames(1);await Task.Delay(16);
            while(peerStates.TryDequeue(out var d))
            {
                Check(d.GetProperty("stageLighting").GetString()==serverLighting,"both network clients receive the same round lighting");
                observed|=d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId && Math.Abs(p.GetProperty("z").GetSingle()-before)>.15);
            }
        }
        collectPeerStates=false;peerStates.Clear();
        Check(observed,"injected engine movement observed by second network client");
        Input(0,default); await Frames(4);
        Check(_root.BattleAudio.Counts.GetValueOrDefault("bomb")==bombCueBefore+1,"network bomb event plays once across repeated snapshots");
        await Capture("network-two-humans");
        var savedGraphics=_root.Preferences.Graphics;
        await SelectGraphics(BlastCircuit.Rendering.GraphicsPreset.Low);
        Check(ReferenceEquals(_root.ActiveSession,host)&&LightingArena.TimeOfDay==BlastCircuit.Arena.StageLighting.FromWire(State.GetProperty("stageLighting").GetString()),"graphics preset preserves online connection and server lighting");
        await SelectGraphics(savedGraphics);
        var savedLighting=_root.Preferences.StageLighting;
        var otherLighting=(BlastCircuit.Arena.StageTime)(((int)LightingArena.TimeOfDay+1)%4);
        await SelectLocalLighting(otherLighting);
        Check(ReferenceEquals(_root.ActiveSession,host)&&LightingArena.TimeOfDay==BlastCircuit.Arena.StageLighting.FromWire(State.GetProperty("stageLighting").GetString()),"personal preference cannot override online server lighting");
        await SelectLocalLighting(savedLighting);
        await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause"&&ReferenceEquals(_root.ActiveSession,host),"online Esc opens menu without disconnecting");
        int pausedSnapshots=_root.SnapshotCount;await Task.Delay(300);await Frames(3);
        Check(_root.SnapshotCount>pausedSnapshots,"online authority continues while menu is open");
        await ActivateNamed("PauseSettings");Check(_root.ScreenName=="Settings"&&ReferenceEquals(_root.ActiveSession,host),"online player settings keep the room connection");
        await KeyPress(Key.Escape);await ActivateNamed("ResumeGame");
        Check(_root.ScreenName=="Match"&&ReferenceEquals(_root.ActiveSession,host),"online menu resumes the same match");
        _root.Back(); await Frames(4);
        await QuickPlayLobby();
    }

    private async Task QuickPlayLobby()
    {
        _root.Navigate("Online");await Frames(3);
        await _root.StartOnlineAsync(new Uri(_server),"Quick Play first",new SessionSettings(Bots:0),NetworkMode.QuickPlay);
        var first=(NetworkSession)_root.ActiveSession!;
        await LobbyUntil(()=>first.Party is {PublicRoom:true,StartAt:null});
        Check(_root.ScreenName=="Lobby"&&VisibleText(_root).Contains("Waiting for a second player"),"lone Quick Play player sees a waiting lobby without a timer");
        Check(!NamedButton("StartRoom").Visible&&!NamedButton("EditRoomSettings").Visible,"anonymous public owner has no manual-start or rule-edit controls");
        Check(VisibleText(_root).Contains("Shared rules")&&!VisibleText(_root).Contains("Room owner:"),"public lobby explains shared rules without presenting an anonymous owner as an operator");
        await Capture("quick-play-waiting-alone");
        // Open personal Settings while alone. Start the real minute only once
        // the expensive page transition has completed, then test the return
        // before captures consume that minute on a software renderer.
        await ActivateNamed("LobbySettings");
        Check(_root.ScreenName=="Settings"&&ReferenceEquals(first,_root.ActiveSession),"personal settings keep the lone Quick Play session");
        await using var second=await NetworkSession.ConnectAsync(new Uri(_server),"Quick Play second",new SessionSettings(Bots:0),NetworkMode.QuickPlay);
        using var liveSecond=KeepFixtureLive(second);
        await LobbyUntil(()=>first.Party?.StartAt is not null);
        var observed=first.DeadlineObserved;
        long initial=first.Party!.StartAt!.Value;
        Check(observed?.StartAt==initial,"the first public deadline is coherently received while Settings is open");
        await KeyPress(Key.Escape);
        Check(_root.ScreenName=="Lobby"&&ReferenceEquals(first,_root.ActiveSession)&&first.Party?.StartAt==initial,
            "personal settings return to the same public countdown without resetting it");
        Check(second.LobbyId==first.LobbyId,"graphical Quick Play joins the same real matchmade lobby");
        Check(VisibleText(_root).Contains("Battle begins in "),"public lobby renders the authoritative countdown");
        string before=VisibleText(_root);
        await Task.Delay(1200);await Frames(3);
        Check(VisibleText(_root)!=before&&_root.ScreenName=="Lobby"&&_root.LatestState is null,"visible countdown advances while gameplay remains stopped");
        await Capture("quick-play-two-players-countdown");
        await using var third=await NetworkSession.ConnectAsync(new Uri(_server),"Quick Play third",new SessionSettings(Bots:0),NetworkMode.QuickPlay);
        using var liveThird=KeepFixtureLive(third);
        await LobbyUntil(()=>first.Party?.StartAt>initial);
        long changed=first.Party!.StartAt!.Value;
        Check(VisibleText(_root).Contains("Quick Play third")&&VisibleText(_root).Contains("Player changes restart"),"late arrival appears alongside the countdown-reset explanation");
        await Capture("quick-play-three-players-countdown");
        await Task.Delay(1100);liveThird.Dispose();await third.DisposeAsync();
        await LobbyUntil(()=>first.Party?.StartAt>changed);
        // Exact 60 s reset and no early start are proven on the server clock in
        // PublicMatchmakingChecks. Here, prove the reset deadline reaches this client
        // coherently: its first socket receipt (frame-independent) still had a fresh
        // minute (transit + local/server clock offset only) and is still the room's
        // current deadline. Receipt-to-check delay is recorded separately.
        var reset=first.DeadlineObserved;long nowMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Event("quick-play-reset-timing",new{deadline=reset?.StartAt,sequence=reset?.Sequence,remainingAtReceiptMs=reset?.StartAt-reset?.ReceivedAtUnixMs,receiptToCheckMs=nowMs-reset?.ReceivedAtUnixMs});
        Check(reset is {StartAt:{} deadline}&&deadline==first.Party!.StartAt&&deadline-reset.ReceivedAtUnixMs>55000&&deadline-reset.ReceivedAtUnixMs<=60500,
            "three-to-two departure shows a fresh full minute");
        liveSecond.Dispose();await second.DisposeAsync();
        await LobbyUntil(()=>first.Party?.StartAt is null);
        Check(VisibleText(_root).Contains("Waiting for a second player")&&!VisibleText(_root).Contains("Battle begins in "),"below two players removes the visible countdown immediately");
        Check(!NamedButton("StartRoom").Visible&&!NamedButton("EditRoomSettings").Visible,"roster changes never restore public owner powers");
        await Capture("quick-play-departure-cancels-countdown");
        // The post-match map screen must keep the same public-room permissions even without a countdown.
        var menu=_root.FindChildren("*","",true,false).OfType<BlastCircuit.UI.Menu.ArcadeMenu>().Single();
        menu.UpdateMapSelection(first.IsLeader,first.Party!);
        Check(NamedButton("StageSelector").Disabled&&NamedButton("CPUrivals").Disabled&&NamedButton("AdvancedSettings").Disabled,
            "Quick Play map selection never grants anonymous owners rule-edit powers when the countdown is cancelled");
        _root.Back();await Frames(4);

        async Task LobbyUntil(Func<bool> condition)
        {
            var elapsed=System.Diagnostics.Stopwatch.StartNew();ulong startFrame=Godot.Engine.GetProcessFrames();
            while(Waiting(elapsed,15,startFrame))
            {
                await Frames(1);await Task.Delay(16);
                if(condition()){await Frames(2);return;}
            }
            throw new TimeoutException("Quick Play lobby condition timed out: "+first.Party);
        }
    }
}
