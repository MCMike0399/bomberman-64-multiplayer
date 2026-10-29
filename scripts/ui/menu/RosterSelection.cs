using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI.Menu;

/// <summary>Eight editable places, explicit runtime control bindings, and live shared-model character previews.</summary>
public partial class RosterSelection : VBoxContainer
{
    public LocalRoster Roster {get;}=new();
    public event Action? ContinueRequested;
    public event Action<int>? DifficultyChanged;
    public event Action? BackRequested;
    public event Action<int>? LineupChanged;
    private readonly List<(PanelContainer Panel,OptionButton Role,Button Appearance,Label Title,Label Binding,Control Preview)> _cards=[];
    private Label _summary=null!,_notice=null!;
    private int _selected;
    private bool _nativePopups;
    private ulong _popupClosedFrame=ulong.MaxValue;
    public int BotDifficulty {get;set;}=2;
    private readonly List<DifficultyPips> _difficultyPips=[];
    private bool _updating;
    public static readonly string[] AppearanceNames=["WHITE","BLACK","RED","BLUE","GREEN","GOLD","PURPLE","CYAN"];
    public Func<int,LineupPose>? LivePose {get;set;}
    public object DiagnosticState=>new {sharedStage=true,humans=Roster.Humans,cpu=Roster.CpuPlayers,selected=_selected,slots=Roster.Slots.Select((s,i)=>new {slot=i,role=s.Role.ToString().ToLowerInvariant(),appearance=s.Appearance,binding=Binding(i),selected=i==_selected,animationTime=Pose(i).Clock,walkPose=Pose(i).WalkPose,previewBob=Pose(i).Bob,previewAppearance=Pose(i).Appearance,previewVisible=Pose(i).Visible}).ToArray()};
    private LineupPose Pose(int slot)=>LivePose?.Invoke(slot)??default;
    private string Binding(int slot)=>Roster.Slots[slot].Role==RosterRole.Human
        ?ControllerHub.Current.BindingLabel(Roster.Slots.Take(slot).Count(s=>s.Role==RosterRole.Human),Roster.Humans)
        :Roster.Binding(slot,ControllerHub.Current.PlayableCount);
    public override void _Ready()
    {
        _nativePopups=OS.GetName()=="Linux"&&DisplayServer.GetName()=="X11";
        AddThemeConstantOverride("separation",10);
        var header=ArcadeTheme.Row(this,20);ArcadeTheme.Heading(header,"Player Select",40).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Quiet(ArcadeTheme.Button(header,"Back",()=>BackRequested?.Invoke())).Name="RosterBack";
        _summary=ArcadeTheme.Text(this,"",17,ArcadeTheme.Muted);
        var grid=new GridContainer {Columns=8,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddThemeConstantOverride("h_separation",3);grid.AddThemeConstantOverride("v_separation",12);AddChild(grid);
        for(int i=0;i<8;i++)
        {
            int slot=i;var panel=new RosterSeatPanel {CustomMinimumSize=new(145,460),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddChild(panel);
            var column=ArcadeTheme.Column(panel,8);
            var title=ArcadeTheme.Text(column,"",22,ArcadeTheme.Gold,true);title.HorizontalAlignment=HorizontalAlignment.Center;
            var preview=new Control {MouseFilter=MouseFilterEnum.Ignore,CustomMinimumSize=new(130,270),SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddChild(preview);
            var row=ArcadeTheme.Column(column,4);
            var role=new OptionButton {Name=$"Slot{slot}Role",CustomMinimumSize=new(0,58),SizeFlagsHorizontal=SizeFlags.ExpandFill};role.AddItem("OFF");role.AddItem("MAN");role.AddItem("COM");role.AddThemeFontSizeOverride("font_size",26);role.Alignment=HorizontalAlignment.Center;
            role.AddThemeIconOverride("arrow",new ImageTexture());row.AddChild(role);
            // Opening after pointer/gamepad navigation must still establish a usable item focus.
            var popup=role.GetPopup();
            // The 4.7.2 Linux release template leaves embedded-popup parent
            // subscriptions behind on close. A native X11 window keeps the
            // same Godot-painted menu without that broken subscription path.
            if(_nativePopups)
            {
                popup.ForceNative=true;
                popup.PopupHide+=()=>_popupClosedFrame=Engine.GetProcessFrames();
            }
            popup.AboutToPopup+=()=>popup.CallDeferred(PopupMenu.MethodName.SetFocusedItem,role.Selected);
            var appearance=new Button {Name=$"Slot{slot}Appearance",CustomMinimumSize=new(0,38),SizeFlagsHorizontal=SizeFlags.ExpandFill,Alignment=HorizontalAlignment.Center};
            ArcadeTheme.Quiet(appearance);appearance.AddThemeFontOverride("font",ArcadeTheme.Body);appearance.AddThemeFontSizeOverride("font_size",15);appearance.AddThemeConstantOverride("outline_size",4);appearance.AddThemeConstantOverride("h_separation",7);row.AddChild(appearance);
            appearance.TooltipText="Change suit colour";
            var pips=new DifficultyPips {Name=$"Slot{slot}Difficulty",CustomMinimumSize=new(0,44),TooltipText="Change CPU level for all bots"};column.AddChild(pips);_difficultyPips.Add(pips);
            pips.Pressed+=()=>{BotDifficulty=BotDifficulty%3+1;DifficultyChanged?.Invoke(BotDifficulty);Select(slot);Refresh();};
            pips.FocusEntered+=()=>Select(slot);
            var binding=ArcadeTheme.Text(column,"",12,ArcadeTheme.Muted);binding.HorizontalAlignment=HorizontalAlignment.Center;
            _cards.Add((panel,role,appearance,title,binding,preview));
            role.ItemSelected+=value=>{if(!_updating){var current=Roster.Slots[slot];Roster.Set(slot,(RosterRole)value,current.Appearance);Select(slot);Refresh();}};
            appearance.Pressed+=()=>{var current=Roster.Slots[slot];Roster.Set(slot,current.Role,(current.Appearance+1)%8);Select(slot);Refresh();};
            role.FocusEntered+=()=>Select(slot);appearance.FocusEntered+=()=>Select(slot);panel.MouseEntered+=()=>Select(slot);
        }
        var footer=ArcadeTheme.Row(this,20);_notice=ArcadeTheme.Text(footer,"",15,ArcadeTheme.Muted);_notice.SizeFlagsHorizontal=SizeFlags.ExpandFill;_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;_notice.CustomMinimumSize=new(760,40);
        ArcadeTheme.Button(footer,"Continue to rules",()=>
        {
            var error=Roster.ValidationError(ControllerHub.Current.PlayableCount);
            if(error!=null){_notice.Text=error;_notice.AddThemeColorOverride("font_color",ArcadeTheme.Gold);return;}
            ContinueRequested?.Invoke();
        },true).Name="RosterContinue";
        ControllerHub.Current.DevicesChanged+=Refresh;Refresh();Select(0);
    }
    public override void _ExitTree(){ControllerHub.Current.DevicesChanged-=Refresh;}
    public void Configure(int slot,RosterRole role,int appearance){Roster.Set(slot,role,appearance);Select(slot);Refresh();}
    public void ConfigureCounts(int humans,int bots)
    {
        for(int i=0;i<8;i++)Roster.Set(i,i<humans?RosterRole.Human:i<humans+bots?RosterRole.Cpu:RosterRole.Off,Roster.Slots[i].Appearance);
        Refresh();
    }
    private void Select(int slot)
    {
        _selected=slot;LineupChanged?.Invoke(slot);
        for(int i=0;i<_cards.Count;i++)
        {
            var card=_cards[i];((RosterSeatPanel)card.Panel).Selected=i==slot;card.Panel.QueueRedraw();
            // The chosen seat gets a gold frame; the others stay open so the 3D lineup shows through.
            var style=ArcadeTheme.Panel(i==slot?new Color(ArcadeTheme.Cream,.12f):new Color(0,0,0,0),i==slot?ArcadeTheme.Gold:new Color(0,0,0,0),i==slot?3:0,18,6);
            card.Panel.AddThemeStyleboxOverride("panel",style);
            bool off=Roster.Slots[i].Role==RosterRole.Off;
            card.Title.AddThemeColorOverride("font_color",i==slot?ArcadeTheme.Gold:off?ArcadeTheme.Muted:ArcadeTheme.Cream);
        }
    }
    public void Refresh()
    {
        if(_cards.Count!=8)return;
        _updating=true;int human=0,cpu=0,pads=ControllerHub.Current.PlayableCount;
        for(int i=0;i<8;i++)
        {
            var slot=Roster.Slots[i];var card=_cards[i];card.Role.Select((int)slot.Role);
            card.Title.Text=slot.Role switch {RosterRole.Human=>$"P{++human}",RosterRole.Cpu=>$"CPU {++cpu}",_=>$"P{i+1}"};
            _difficultyPips[i].Level=slot.Role==RosterRole.Cpu?BotDifficulty:0;
            _difficultyPips[i].Disabled=slot.Role!=RosterRole.Cpu;
            StyleRole(card.Role,slot.Role);
            card.Appearance.Text=ArcadeTheme.Word(AppearanceNames[slot.Appearance]);card.Appearance.Icon=ArcadeIcons.Swatch(ArcadeTheme.Players[slot.Appearance],18);
            card.Binding.Text=SeatHint(slot.Role==RosterRole.Human?ControllerHub.Current.BindingLabel(human-1,Roster.Humans):Roster.Binding(i,pads));
            card.Binding.ClipText=true;card.Binding.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            card.Binding.TooltipText=card.Binding.Text;
            card.Appearance.Modulate=slot.Role==RosterRole.Off?new Color(.65f,.65f,.65f):Colors.White;
        }
        _summary.Text=$"{Roster.Participants} bombers: {Roster.Humans} {(Roster.Humans==1?"human":"humans")}, {Roster.CpuPlayers} CPU. {pads} {(pads==1?"gamepad":"gamepads")} connected.";
        _notice.Text="MAN is a human player, COM is the computer, OFF leaves the seat empty.";_notice.AddThemeColorOverride("font_color",ArcadeTheme.Muted);
        _updating=false;Select(_selected);
    }
    /// <summary>MAN is a coral key, COM an indigo key and OFF an empty outline, so seats read at a glance.</summary>
    private static void StyleRole(OptionButton role,RosterRole value)
    {
        var face=value switch {RosterRole.Human=>new Color("d9534a"),RosterRole.Cpu=>ArcadeTheme.Indigo,_=>new Color(0,0,0,0)};
        if(value==RosterRole.Off)
        {
            var hollow=ArcadeTheme.Panel(new Color(ArcadeTheme.Cream,.05f),new Color(ArcadeTheme.Cream,.3f),2,12,6);
            foreach(var state in new[]{"normal","pressed","hover_pressed"})role.AddThemeStyleboxOverride(state,hollow);
            role.AddThemeStyleboxOverride("hover",ArcadeTheme.Panel(new Color(ArcadeTheme.Cream,.14f),new Color(ArcadeTheme.Cream,.5f),2,12,6));
        }
        else
        {
            role.AddThemeStyleboxOverride("normal",ArcadeTheme.Key(face,false,false,12,6));role.AddThemeStyleboxOverride("hover",ArcadeTheme.Key(face.Lightened(.15f),false,false,12,6));
            role.AddThemeStyleboxOverride("pressed",ArcadeTheme.Key(face,true,false,12,6));role.AddThemeStyleboxOverride("hover_pressed",ArcadeTheme.Key(face,true,false,12,6));
        }
        role.AddThemeStyleboxOverride("focus",ArcadeTheme.FocusRing());
        var text=value==RosterRole.Off?ArcadeTheme.Muted:ArcadeTheme.Cream;
        foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"})role.AddThemeColorOverride(state,text);
        role.AddThemeConstantOverride("outline_size",value==RosterRole.Off?0:6);
    }
    /// <summary>Device bindings come from shared code in capitals; seats show them as plain hints.</summary>
    private static string SeatHint(string binding)=>binding switch
    {
        "EMPTY SEAT"=>"Empty seat","CPU CONTROLLED"=>"Computer","CONNECT GAMEPAD"=>"Connect a gamepad",
        _=>binding.Replace(" • CONNECT PAD",", connect a pad").Replace("GAMEPAD","Gamepad").Replace("  •  ",", ").Replace(" • ",", ")
    };
    public bool HasOpenPopup=>_cards.Any(c=>c.Role.GetPopup().Visible);
    public bool OwnsPopupInput=>_nativePopups&&(HasOpenPopup||_popupClosedFrame==Engine.GetProcessFrames());
    public Control[][] FocusColumns=>_cards.Select((c,i)=>new Control[]{c.Role,c.Appearance,_difficultyPips[i]}).ToArray();
}


public readonly record struct LineupPose(double Clock,float WalkPose,float Bob,int Appearance,bool Visible);
