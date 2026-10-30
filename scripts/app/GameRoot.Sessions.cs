using Godot;
using Bomber.Client.Controls;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.InputDevices;
namespace BlastCircuit.App;

/// <summary>Local and online session lifetime: start, replay, connection cancellation and disposal.</summary>
public partial class GameRoot
{
    // Local-only QA seam; ordinary starts and replays leave this unset.
    internal int? LocalMatchSeedOverride {get;set;}

    private void StartConfigured()
    {
        if(_postMatchSetup)
        {
            if(_session is NetworkSession {IsLeader:true,Party.Phase:"lobby",Party.CanStart:true} next)
            {
                var selected=_menu.OnlineSettings;
                // Unchanged settings do not emit another status in adaptive rooms.
                if(next.Party.Settings is {} accepted&&SessionSettings.FromWire(accepted)==selected)next.RequestStart();
                else {_startAfterSettings=selected;next.SetRoomSettings(selected);}
            }
            return;
        }
        if(_editingRoom)
        {
            if(_session is NetworkSession {IsLeader:true,Party.Phase:"lobby"} room)room.SetRoomSettings(_menu.OnlineSettings);
            _editingRoom=false;ShowPartyLobby();return;
        }
        if(_setupForHost)
        {
            // Save advances to Online even when the editor was opened directly.
            // Drop its history entry so the next Back cannot reopen the editor.
            while(_menuHistory.TryPop(out var previous)&&previous!="Online"){}
            Navigate("Online",restoreFocus:true);return;
        }
        int humans=_menu.HumanCount;
        if(humans>LocalControllerLayout.Capacity(ControllerHub.Current.PlayableCount))
        {_menu.Notice($"Connect more gamepads for {humans} human players. Two keyboard seats are available.");return;}
        StartLocal(_menu.Settings,humans,_menu.Practice,_menu.PlayerAppearances);
    }

    public void StartLocal(SessionSettings settings,int humans=1,bool practice=false,IReadOnlyList<int>? playerAppearances=null,int? seed=null)
    {
        if(_connecting)return;
        try
        {
            // Ordinary starts/replays choose a fresh layout; QA can explicitly reproduce a seed.
            int matchSeed=seed??(!practice?LocalMatchSeedOverride:null)??System.Random.Shared.Next();
            var session=new LocalSession(settings,humans,seed:matchSeed,practice:practice,playerAppearances:playerAppearances);
            _lastSettings=settings;_lastHumans=humans;_lastAppearances=playerAppearances?.ToArray();_practice=practice;_lastServer=null;
            Begin(session,false);
        }
        catch(Exception error){_menu.Notice(error.Message);GD.PushWarning(error.Message);}
    }

    private async void StartConfiguredOnline(NetworkMode mode)
    {
        try{await StartOnlineAsync(new Uri(_menu.ServerUrl),_menu.PlayerName,_menu.OnlineSettings,mode,_menu.Code,appearance:_menu.OnlineAppearance);}
        catch(Exception error){_menu.SetBusy(false,"Could not connect: "+error.Message);}
    }

    public async Task StartOnlineAsync(Uri server,string name,SessionSettings settings,NetworkMode mode,string? code=null,int appearance=0)
    {
        if(_connecting)throw new InvalidOperationException("A connection is already in progress.");
        ReleaseSession();_menu.SetOnlineAppearance(appearance);_connecting=true;_practice=false;
        var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(120));_connectCancel=cancellation;
        ScreenName="Online";_menu.Visible=true;_arena.Visible=false;_hud.Visible=false;_menu.ShowScreen("Online");_menu.SetBusy(true,mode==NetworkMode.QuickPlay?"Searching for players... Cancel to leave the queue.":"Connecting to your battle room...");
        try
        {
            var session=await NetworkSession.ConnectAsync(server,name,settings,mode,code,cancellation.Token,appearance,() => new GodotPeerLink(useStun:!server.IsLoopback));
            if(cancellation.IsCancellationRequested||!IsInsideTree()){await session.DisposeAsync();return;}
            _lastSettings=settings;_lastHumans=1;_lastServer=server;_lastName=name;_lastOnlineAppearance=appearance;_lastMode=mode;_lastCode=code??"";
            Begin(session,true);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){if(IsInsideTree())_menu.Notice("Connection cancelled. Choose a mode to try again.");}
        catch(Exception error){if(IsInsideTree())_menu.Notice("Could not connect: "+error.Message);throw;}
        finally
        {
            if(ReferenceEquals(_connectCancel,cancellation)){_connectCancel=null;_connecting=false;if(IsInsideTree())_menu.SetBusy(false);}
            cancellation.Dispose();
        }
    }

    private void Begin(ISession session,bool online)
    {
        _uiAudio.MenuConfirm();
        ReleaseSession();_arena.ResetMatch();_session=session;LatestStage=null;LatestState=null;LatestParty=null;FirstNetworkStateServerTime=null;_result=null;_lobby=null;SnapshotCount=0;_injected.Clear();_hud.Reset();_hud.Practice=_practice;_menu.ResetLobby();_partyMatch=-1;_networkGeneration=0;
        ControllerHub.Current.BeginMatch(session.LocalPlayerCount);
        ApplyStageLighting();
        if(online)
        {ScreenName="Lobby";_menu.ShowScreen("Lobby");_menu.ShowLobby(null,session.JoinCode,session.Status);_menu.Visible=true;_hud.Visible=false;_arena.Visible=false;}
        else EnterMatch();
        if(!online)GetViewport().GuiReleaseFocus();
    }

    private void EnterMatch()
    {
        _postMatchSetup=false;_startAfterSettings=null;_editingRoom=false;ScreenName="Match";_menu.Visible=false;_arena.Visible=true;_hud.Visible=true;GetViewport().GuiReleaseFocus();ShowTutorialHud();
    }

    public void InjectInput(int seat,PlayerInput? input)
    {
        if(input is { } value)_injected[seat]=()=>value;else _injected.Remove(seat);
    }

    // QA providers may expire a synthetic hold before a stalled frame republishes it.
    // Real device sampling remains in PlayerControls.
    public void InjectInputProvider(int seat,Func<PlayerInput> input)=>_injected[seat]=input;

    private void ContinueAfterResults()
    {
        if(_session is NetworkSession {Party:not null} room)
        {
            if(room.Party.Phase=="lobby")ShowMapSelection();
            return;
        }
        if(_session is LocalSession)
        {
            Navigate("Setup");return;
        }
        Replay();
    }

    private void ShowMapSelection()
    {
        if(_session is not NetworkSession {Party.Settings:{} rules} room)return;
        _session.ReleaseInputs();ControllerHub.Current.StopVibration();ControllerHub.Current.RearmControls();
        _postMatchSetup=true;_editingRoom=true;_setupForHost=true;
        _menu.Configure(SessionSettings.FromWire(rules));
        ScreenName="Setup";_arena.Visible=false;_hud.Visible=false;_menu.SetOverlay(false);_menu.Visible=true;
        _menu.ShowScreen("RematchSetup");RefreshMapSelection();
    }

    private void RefreshMapSelection()
    {
        if(_session is not NetworkSession {Party:{} party} room)return;
        if(!room.IsLeader&&party.Settings is {} rules)_menu.Configure(SessionSettings.FromWire(rules));
        _menu.UpdateMapSelection(room.IsLeader,party);
    }

    private void Replay()
    {
        if(_trainingSession){StartTutorial(_tutorialLessonId??"free");return;}
        if(_session is NetworkSession {Party:not null}){ShowPartyLobby();return;}
        ReleaseSession();
        if(_lastServer is { } server)
        {
            // Quick play finds a fresh match; a private host gets a fresh shareable room.
            if(_lastMode==NetworkMode.JoinCode){Navigate("Online");_menu.Notice("Enter a new room code to join your next battle.");}
            else _=ReplayOnline(server);
        }
        else StartLocal(_lastSettings,_lastHumans,_practice,_lastAppearances);
    }

    private async Task ReplayOnline(Uri server)
    {try{await StartOnlineAsync(server,_lastName,_lastSettings,_lastMode,_lastCode,_lastOnlineAppearance);}catch(Exception error){_menu.Notice(error.Message);}}

    private void CancelConnection()
    {
        var cancellation=_connectCancel;_connectCancel=null;
        if(_connecting)_menu.SetBusy(false);
        _connecting=false;cancellation?.Cancel();
    }

    private void ReleaseSession()
    {
        var old=_session;_session=null;_injected.Clear();_postMatchSetup=false;_startAfterSettings=null;
        _tutorialLessonId=null;_trainingSession=false;if(_tutorialHud!=null)_tutorialHud.Visible=false;
        ControllerHub.Current.EndMatch();
        if(old!=null)_=DisposeSessionAsync(old);
    }

    private static async Task DisposeSessionAsync(ISession session)
    {try{await session.DisposeAsync();}catch(Exception error){GD.PushWarning("Session cleanup: "+error.Message);}}
}
