using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
namespace BlastCircuit.UI.Menu;

/// <summary>Mode select, local roster, match setup and advanced-rule screens.</summary>
public partial class ArcadeMenu
{
    private void BuildModes()
    {
        var screen=Screen("Modes");
        // The wordmark tilts slightly, with a lit bomb hanging off "Blast" as its one moving part.
        var wordmark=new Control {Position=new(104,78),Rotation=-.045f,MouseFilter=MouseFilterEnum.Ignore};screen.AddChild(wordmark);
        var title=ArcadeTheme.Heading(wordmark,"Blast\nCircuit",100);title.AddThemeConstantOverride("line_spacing",-30);
        float blastWidth=ArcadeTheme.Display.GetStringSize("Blast",HorizontalAlignment.Left,-1,100).X;
        wordmark.AddChild(new LitBomb {Position=new(blastWidth+4,-14),Size=new(116,116)});
        var menu=new VBoxContainer {Position=new(748,174),Size=new(560,620)};menu.AddThemeConstantOverride("separation",9);screen.AddChild(menu);
        Mode(menu,"Local Battle",()=>Navigation?.Invoke("Roster"),Coral,ArcadeTheme.Gold);
        Mode(menu,"Online Battle",()=>Navigation?.Invoke("Online"),Lagoon,ArcadeTheme.Cream);
        Mode(menu,"Tutorial",()=>Navigation?.Invoke("Tutorial"),ArcadeTheme.Indigo,ArcadeTheme.Pink);
        var practice=ArcadeTheme.Quiet(ArcadeTheme.Button(menu,"Practice",()=>Navigation?.Invoke("Practice")));practice.CustomMinimumSize=new(0,48);practice.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;
        var controls=ArcadeTheme.Quiet(ArcadeTheme.Button(menu,"Controls",()=>Navigation?.Invoke("Controls")));controls.Name="OpenControls";controls.CustomMinimumSize=new(0,48);
        controls.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;
        var settings=ArcadeTheme.Quiet(ArcadeTheme.Button(menu,"Settings",()=>Navigation?.Invoke("Settings")));settings.Name="OpenSettings";settings.CustomMinimumSize=new(0,48);settings.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;_settingsButton=settings;
        RegisterNavigation("Modes",[],[],Choices(menu));
    }

    private void BuildControls()
    {
        var screen=Screen("Controls");var box=Card(screen,new(64,54),new(1312,746));
        var panel=new ControllerPanel();box.AddChild(panel);panel.BackRequested+=()=>BackRequested?.Invoke();
        RegisterNavigation("Controls",[],[],Choices(panel));
    }

    private static ArcadeModeButton Mode(Node parent,string title,Action action,Color face,Color accent)
    {
        var button=new ArcadeModeButton {Text=title,CustomMinimumSize=new(0,116),FaceColor=face,Accent=accent};
        button.Pressed+=action;parent.AddChild(button);return button;
    }

    private void BuildRoster()
    {
        var screen=Screen("Roster");var panel=new PanelContainer {Position=new(64,54),Size=new(1312,746)};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color(0,0,0,0),ArcadeTheme.Ink,0,0,12));screen.AddChild(panel);
        _localPlayers=new RosterSelection();panel.AddChild(_localPlayers);_localPlayers.LivePose=_background.Pose;_localPlayers.LineupChanged+=(slot)=>_background.ConfigureRoster(_localPlayers.Roster,slot);_localPlayers.ContinueRequested+=()=>Navigation?.Invoke("Setup");_localPlayers.BackRequested+=()=>BackRequested?.Invoke();
        RegisterNavigation("Roster",[(Control)screen.FindChild("RosterBack",true,false)],[(Control)screen.FindChild("RosterContinue",true,false)],_localPlayers.FocusColumns);
    }

    public void ConfigureRosterSlot(int slot,RosterRole role,int appearance)=>_localPlayers.Configure(slot,role,appearance);

    private void BuildSetup()
    {
        var screen=Screen("Setup");
        _setupTitle=ArcadeTheme.Heading(screen,"Match Setup",48);_setupTitle.Position=new(80,58);
        var back=ArcadeTheme.Quiet(ArcadeTheme.Button(screen,"Back",()=>BackRequested?.Invoke()));back.Name="SetupBack";back.Position=new(1195,73);back.Size=new(140,48);
        // These native settings nodes remain the single source of truth for configure/API/harness callers.
        var state=new VBoxContainer {Name="SetupSettings",Visible=false};screen.AddChild(state);
        _stage=new OptionButton {Name="Stage"};foreach(var title in new[]{"Sunken Temple","Rock Garden","Up & Down","Top Rules","Blizzard Battle","Greedy Trap"})_stage.AddItem(title);state.AddChild(_stage);
        _wins=new SpinBox {Name="Wins",MinValue=1,MaxValue=5,Value=2};state.AddChild(_wins);
        _bots=new SpinBox {Name="Bots",MinValue=0,MaxValue=7,Value=3};state.AddChild(_bots);
        _rounds=new SpinBox {Name="RoundSeconds",MinValue=60,MaxValue=300,Step=30,Value=120};state.AddChild(_rounds);
        _difficulty=new OptionButton {Name="Difficulty"};foreach(var title in new[]{"Easy","Medium","Hard"})_difficulty.AddItem(title);_difficulty.Select(1);state.AddChild(_difficulty);
        _sudden=new CheckButton {Name="SuddenDeath",ButtonPressed=true};state.AddChild(_sudden);
        _ghosts=new CheckButton {Name="Ghosts",ButtonPressed=true};state.AddChild(_ghosts);
        _stageDial=new ArcadeRuleDial {Name="StageSelector",Position=new(80,602),Size=new(640,76),Caption="Arena",ValueWidth=300,ReadValue=()=>_stage.GetItemText(_stage.Selected),ChangeValue=direction=>{_stage.Select((_stage.Selected+direction+StageIds.Length)%StageIds.Length);UpdateStageDescription();RoomSettingsChanged?.Invoke();}};
        screen.AddChild(_stageDial);
        _stageDescription=ArcadeTheme.Text(screen,"",18);_stageDescription.Position=new(104,694);_stageDescription.Size=new(620,70);
        _stage.ItemSelected+=_=>UpdateStageDescription();
        _rosterSummary=ArcadeTheme.Text(screen,"",18,ArcadeTheme.Muted);_rosterSummary.Position=new(822,140);
        var rail=new VBoxContainer {Position=new(800,184),Size=new(535,415)};rail.AddThemeConstantOverride("separation",3);screen.AddChild(rail);
        Dial(rail,"Wins to victory",()=>((int)_wins.Value).ToString(),direction=>_wins.Value=Cycle((int)_wins.Value,direction,1,5));
        var roundTime=Dial(rail,"Normal + sudden",()=>_sudden.ButtonPressed?$@"{TimeSpan.FromSeconds(_rounds.Value-60):m\:ss} + 1:00":TimeSpan.FromSeconds(_rounds.Value).ToString(@"m\:ss"),direction=>_rounds.Value=Cycle((int)_rounds.Value,direction*30,60,300));
        roundTime.Name="Roundtime";_sudden.Toggled+=enabled=>roundTime.Caption=enabled?"Normal + sudden":"Round time";
        Dial(rail,"CPU level",()=>_difficulty.GetItemText(_difficulty.Selected),direction=>_difficulty.Select((_difficulty.Selected+direction+3)%3));
        var bots=Dial(rail,"CPU rivals",()=>((int)_bots.Value).ToString(),direction=>_bots.Value=Cycle((int)_bots.Value,direction,0,7));_hostBotBox=bots;
        Dial(rail,"Sudden death",()=>_sudden.ButtonPressed?"On":"Off",_=>_sudden.ButtonPressed=!_sudden.ButtonPressed);
        Dial(rail,"Ghost comeback",()=>_ghosts.ButtonPressed?"On":"Off",_=>_ghosts.ButtonPressed=!_ghosts.ButtonPressed);
        _editPlayers=ArcadeTheme.Quiet(ArcadeTheme.Button(rail,"Change players",()=>Navigation?.Invoke("Roster")));_editPlayers.Name="EditPlayers";_editPlayers.CustomMinimumSize=new(0,43);_editPlayers.AddThemeFontSizeOverride("font_size",16);
        _editPlayers.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;
        var advanced=ArcadeTheme.Quiet(ArcadeTheme.Button(rail,"Advanced settings",()=>Navigation?.Invoke("Advanced")));advanced.Name="AdvancedSettings";advanced.CustomMinimumSize=new(0,43);advanced.AddThemeFontSizeOverride("font_size",16);
        _notice=ArcadeTheme.Text(screen,"",16);_notice.Position=new(824,625);_notice.Size=new(490,58);_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _launch=new ArcadeModeButton {Name="LaunchMatch",Text="Start Battle",Position=new(797,699),Size=new(550,86),FaceColor=Coral,Accent=ArcadeTheme.Gold};screen.AddChild(_launch);_launch.Pressed+=()=>LocalRequested?.Invoke();
        RegisterNavigation("Setup",[back],[_launch],[_stageDial],Choices(rail));
        UpdateStageDescription();
    }

    public void UpdateMapSelection(bool leader,PartyStatus party)
    {
        bool editable=leader&&party.Phase=="lobby"&&!party.PublicRoom;
        _stageDial.Disabled=!editable;
        foreach(var dial in _ruleDials)dial.Disabled=!editable;
        ((Button)_screens["Setup"].FindChild("AdvancedSettings",true,false)).Disabled=!editable;
        _launch.Disabled=!editable||!party.CanStart;
        _launch.Text=party.PublicRoom?"Starting automatically":leader?"Start Battle":"Waiting for host";
        if(party.PublicRoom)_launch.Disabled=true;
        _rosterSummary.Text=$"Same room • {party.Bots} CPU rivals";
        _notice.Text=party.PublicRoom?"The next battle starts automatically when the room is ready.":leader?"Choose an arena and start the next battle with this room.":"The room host chooses the next arena. You stay in the same room.";
        RefreshNavigation();
    }

    private void BuildAdvanced()
    {
        var screen=Screen("Advanced");var box=Card(screen,new(64,42),new(1312,758));
        ArcadeTheme.Heading(box,"Advanced match settings",38);
        Copy(box,"Each destroyed block rolls independently. Uncollected items disappear after their lifetime.",18);
        var columns=ArcadeTheme.Row(box,40);columns.SizeFlagsVertical=SizeFlags.ExpandFill;
        var rules=ArcadeTheme.Column(columns,4);rules.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var items=ArcadeTheme.Column(columns,10);items.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        void Rule(Node parent,string name,Func<string> read,Action<int> change)
        {parent.AddChild(new ArcadeRuleDial{Name=name.Replace(" ",""),Caption=name,ReadValue=read,ChangeValue=change,ValueWidth=125});}
        Rule(rules,"Item drop chance",()=>$"{_advanced.DropPercent}%",d=>_advanced=_advanced with{DropPercent=Cycle(_advanced.DropPercent,d*5,0,100)});
        Rule(rules,"Block amount",()=>$"{_advanced.BlockPercent}%",d=>_advanced=_advanced with{BlockPercent=Cycle(_advanced.BlockPercent,d*5,0,100)});
        Rule(rules,"Item lifetime",()=>$"{_advanced.ItemSeconds}s",d=>_advanced=_advanced with{ItemSeconds=Cycle(_advanced.ItemSeconds,d*5,5,60)});
        Rule(rules,"Blast stack cap",()=>_advanced.MaxFire.ToString(),d=>_advanced=_advanced with{MaxFire=Cycle(_advanced.MaxFire,d,1,5)});
        Rule(rules,"Bomb stack cap",()=>_advanced.MaxBombs.ToString(),d=>_advanced=_advanced with{MaxBombs=Cycle(_advanced.MaxBombs,d,1,8)});
        Rule(rules,"Buff / debuff duration",()=>$"{_advanced.EffectSeconds}s",d=>_advanced=_advanced with{EffectSeconds=Cycle(_advanced.EffectSeconds,d*5,5,30)});
        // Notes line up with the dial captions, not the dial hit area.
        Hint(Inset(rules,22,12),"Tiny: smaller and faster. Big: larger and slower. Haste and Slow change movement speed only. A new effect replaces the previous one.");
        Section(items,"Allowed items");
        var grid=new GridContainer{Columns=2};grid.AddThemeConstantOverride("h_separation",14);grid.AddThemeConstantOverride("v_separation",10);items.AddChild(grid);
        foreach(var type in AdvancedRules.ItemTypes)
        {
            string label=type switch{"fire"=>"Blast up","bomb"=>"Bomb up","remote"=>"Remote","red"=>"Power bomb","bounce"=>"Bounce","heart"=>"Heart","focus"=>"Focus","tiny"=>"Tiny","big"=>"Big","haste"=>"Haste",_=>"Slow"};
            var toggle=new ArcadeToggle{Name="Allow_"+type,Text=label,CustomMinimumSize=new(0,52),SizeFlagsHorizontal=SizeFlags.ExpandFill,ButtonPressed=true};grid.AddChild(toggle);
            toggle.Toggled+=enabled=>{var disabled=AdvancedRules.ItemTypes.Where(t=>t==type?!enabled:!_advanced.Allows(t));_advanced=_advanced with{DisabledItems=string.Join(',',disabled)};};
        }
        Hint(Inset(items,0,4),"Disabled items cannot appear in blocks, training pickups or equipment drops. With every item off, blocks drop nothing.");
        var actions=ArcadeTheme.Row(box,18);
        var reset=ArcadeTheme.Quiet(ArcadeTheme.Button(actions,"Restore defaults",()=>{_advanced=new();foreach(string type in AdvancedRules.ItemTypes)((CheckButton)_screens["Advanced"].FindChild("Allow_"+type,true,false)).SetPressedNoSignal(true);}));
        reset.Name="ResetAdvanced";reset.CustomMinimumSize=new(240,52);
        actions.AddChild(new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill});
        var back=ArcadeTheme.Button(actions,"Back to match settings",()=>BackRequested?.Invoke());back.Name="AdvancedBack";back.CustomMinimumSize=new(420,52);
        var itemChoices=Choices(grid);
        RegisterNavigation("Advanced",[],Choices(actions),Choices(rules),itemChoices.Where((_,i)=>i%2==0).ToArray(),itemChoices.Where((_,i)=>i%2==1).ToArray());
    }

    public void ShowAdvanced()
    {
        _advancedReturn=_page=="RematchSetup"?"RematchSetup":_practice?"Practice":_hostBotBox.Visible?"HostSetup":"Setup";
        ShowScreen("Advanced");
        foreach(string type in AdvancedRules.ItemTypes)
            ((CheckButton)_screens["Advanced"].FindChild("Allow_"+type,true,false)).SetPressedNoSignal(_advanced.Allows(type));
    }

    public void CloseAdvanced()=>ShowScreen(_advancedReturn,restoreFocus:true);

    private ArcadeRuleDial Dial(Node parent,string caption,Func<string> value,Action<int> change)
    {
        var dial=new ArcadeRuleDial {Name=caption.Replace(" ",""),Caption=caption,ReadValue=value,ChangeValue=direction=>{change(direction);RoomSettingsChanged?.Invoke();}};parent.AddChild(dial);_ruleDials.Add(dial);return dial;
    }

    private static int Cycle(int value,int direction,int min,int max)=>value+direction>max?min:value+direction<min?max:value+direction;

    public void Configure(SessionSettings settings,int humans=1)
    {
        _advanced=settings.Advanced;
        _hostingPreference=Math.Max(0,Array.IndexOf(new[]{"auto","server","peer"},settings.Hosting));
        _difficulty.Select(Array.IndexOf(new[]{"easy","normal","hard"},settings.BotDifficulty=="medium"?"normal":settings.BotDifficulty));_stage.Select(Math.Max(0,Array.IndexOf(StageIds,settings.Stage)));_bots.Value=settings.Bots;_wins.Value=settings.Wins;_rounds.Value=settings.RoundSeconds;_sudden.ButtonPressed=settings.SuddenDeath;_ghosts.ButtonPressed=settings.Ghosts;_localPlayers.ConfigureCounts(humans,settings.Bots);UpdateStageDescription();
    }

    private void UpdateStageDescription()
    {
        _background.SelectArena(StageIds[_stage.Selected]);
        if(_onlineStage!=null){_onlineStage.StageIndex=_stage.Selected;_onlineStage.QueueRedraw();}
        _stageDescription.Text=_stage.Selected switch {0=>"Climb the outer walkways. Send a pumped bomb\nover the ledge. Watch the water below.",1=>"An open arena with pillars and breakable blocks.\nFind an escape before lighting the fuse.",2=>"Battle over the sea on three suspended levels.\nUse purple stairs to reach the lower item decks.",4=>"Brave changing winds on a frozen snowfield.\nBreak ice cones. Escape the closing avalanches.",3=>"Rule the castle bridges or fight beneath them.\nFour corner staircases connect both floors.",_=>"Risk the recessed chamber for its eight blocks.\nOnly two stairways lead back to the upper court."};
    }
}
