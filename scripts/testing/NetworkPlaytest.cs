using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Audio;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.UI.Hud;
namespace BlastCircuit.Testing;

/// <summary>Real HTTP/WebSocket host and guest sessions against an independent server.</summary>
public partial class PlaytestDriver
{
    private async Task Network()
    {
        _root.Navigate("Online");_root.SetOnlineAppearance(3);await Frames(3);
        await ActivateNamed("OnlineAppearance");
        Check(_root.SelectedAppearance==4,"online suit button selects the host appearance");
        await _root.StartOnlineAsync(new Uri(_server),"Harness host",new SessionSettings(Stage:"sunken-temple",Bots:0),NetworkMode.CreatePrivate,appearance:_root.SelectedAppearance);
        Check(_root.ActiveSession is NetworkSession,"engine uses product network adapter");
        var host=(NetworkSession)_root.ActiveSession!;
        Check(!string.IsNullOrWhiteSpace(host.JoinCode),"network host exposes join code");
        await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Harness peer",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,appearance:6);
        Check(peer.LobbyId==host.LobbyId,"second product client joins same kernel lobby");
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
        await ActivateNamed("CopyRoomCode");
        Check(DisplayServer.ClipboardGet()==host.JoinCode,"copy button writes the actual room code to clipboard");
        Check(NamedButton("CopyRoomCode").Text=="Copied!","copy button confirms success");
        await Capture("network-lobby");
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
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1 && s.GetProperty("players").GetArrayLength()==2,"engine receives two-human authoritative fight",900);
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
        Input(0,new PlayerInput(0,-1,A:true));
        bool observed=false;
        for(var i=0;i<150 && !observed;i++)
        {
            await Frames(1);await Task.Delay(16);
            foreach(var message in peer.DrainMessages())
                if(message.GetProperty("t").GetString()=="state")
                {
                    var d=message.GetProperty("d");
                    Check(d.GetProperty("stageLighting").GetString()==serverLighting,"both network clients receive the same round lighting");
                    observed=d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId && Math.Abs(p.GetProperty("z").GetSingle()-before)>.15);
                }
        }
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
    }
}
