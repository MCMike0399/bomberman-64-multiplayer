using Godot;
using Bomber.Client;
using BlastCircuit.Rendering;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI;

/// <summary>Eight editable places, explicit runtime control bindings, and live shared-model character previews.</summary>
public partial class RosterSelection : VBoxContainer
{
    public LocalRoster Roster {get;}=new();
    public event Action? ContinueRequested;
    public event Action? BackRequested;
    public event Action<int>? LineupChanged;
    private readonly List<(PanelContainer Panel,OptionButton Role,Button Appearance,Label Title,Label Binding,Control Preview)> _cards=[];
    private Label _summary=null!,_notice=null!;
    private int _selected;
    public int BotDifficulty {get;set;}=2;
    private readonly List<Label> _difficultyPips=[];
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
        AddThemeConstantOverride("separation",10);
        var header=ArcadeTheme.Row(this,20);ArcadeTheme.Text(header,"PLAYER SELECT",34,ArcadeTheme.Gold).SizeFlagsHorizontal=SizeFlags.ExpandFill;

        ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke()).Name="RosterBack";
        _summary=ArcadeTheme.Text(this,"",17);
        var grid=new GridContainer {Columns=8,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddThemeConstantOverride("h_separation",3);grid.AddThemeConstantOverride("v_separation",12);AddChild(grid);
        for(int i=0;i<8;i++)
        {
            int slot=i;var panel=new RosterSeatPanel {CustomMinimumSize=new(145,460),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddChild(panel);
            var column=ArcadeTheme.Column(panel,8);
            var title=ArcadeTheme.Text(column,"",22,ArcadeTheme.Gold);title.HorizontalAlignment=HorizontalAlignment.Center;
            var preview=new Control {MouseFilter=MouseFilterEnum.Ignore,CustomMinimumSize=new(130,270),SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddChild(preview);
            var row=ArcadeTheme.Column(column,4);
            var role=new OptionButton {Name=$"Slot{slot}Role",CustomMinimumSize=new(0,58),SizeFlagsHorizontal=SizeFlags.ExpandFill};role.AddItem("OFF");role.AddItem("MAN");role.AddItem("COM");role.AddThemeFontSizeOverride("font_size",34);role.AddThemeColorOverride("font_outline_color",new Color("080b1e"));role.AddThemeConstantOverride("outline_size",7);row.AddChild(role);
            // Opening after pointer/gamepad navigation must still establish a usable item focus.
            var popup=role.GetPopup();
            popup.AboutToPopup+=()=>popup.CallDeferred(PopupMenu.MethodName.SetFocusedItem,role.Selected);
            var appearance=new Button {Name=$"Slot{slot}Appearance",CustomMinimumSize=new(0,38),SizeFlagsHorizontal=SizeFlags.ExpandFill};appearance.AddThemeFontSizeOverride("font_size",12);row.AddChild(appearance);
            StripControl(role);StripControl(appearance);appearance.TooltipText="Cycle suit color";
            var pips=new Label {Text="● ● ●",HorizontalAlignment=HorizontalAlignment.Center};pips.AddThemeFontSizeOverride("font_size",19);pips.AddThemeColorOverride("font_color",new Color("bcbeef"));column.AddChild(pips);_difficultyPips.Add(pips);
            var binding=ArcadeTheme.Text(column,"",11);binding.HorizontalAlignment=HorizontalAlignment.Center;
            _cards.Add((panel,role,appearance,title,binding,preview));
            role.ItemSelected+=value=>{if(!_updating){var current=Roster.Slots[slot];Roster.Set(slot,(RosterRole)value,current.Appearance);Select(slot);Refresh();}};
            appearance.Pressed+=()=>{var current=Roster.Slots[slot];Roster.Set(slot,current.Role,(current.Appearance+1)%8);Select(slot);Refresh();};
            role.FocusEntered+=()=>Select(slot);appearance.FocusEntered+=()=>Select(slot);panel.MouseEntered+=()=>Select(slot);
        }
        var footer=ArcadeTheme.Row(this,20);_notice=ArcadeTheme.Text(footer,"",15);_notice.SizeFlagsHorizontal=SizeFlags.ExpandFill;_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;_notice.CustomMinimumSize=new(760,40);
        ArcadeTheme.Button(footer,"CONTINUE TO RULES   →",()=>
        {
            var error=Roster.ValidationError(ControllerHub.Current.PlayableCount);
            if(error!=null){_notice.Text=error;return;}
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
            var color=ArcadeTheme.Players[Roster.Slots[i].Appearance];
            var style=ArcadeTheme.Panel(i==slot?new Color(1,1,1,.22f):new Color(0,0,0,0),i==slot?new Color("f3e89b"):new Color(color,0),0,0,5);style.BorderWidthBottom=i==slot?4:0;style.ShadowSize=0;
            card.Panel.AddThemeStyleboxOverride("panel",style);
            card.Title.AddThemeColorOverride("font_color",i==slot?ArcadeTheme.Red:ArcadeTheme.Gold);
            card.Title.Text=(i==slot?"▼ ":"")+card.Title.Text.TrimStart('▼',' ');
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
            _difficultyPips[i].Text=slot.Role==RosterRole.Cpu?string.Join(" ",Enumerable.Range(0,3).Select(n=>n<BotDifficulty?"●":"○")):" ";
            _difficultyPips[i].TooltipText="Computer difficulty is chosen in Battle Options.";
            card.Role.AddThemeColorOverride("font_color",slot.Role==RosterRole.Human?new Color("ff7068"):slot.Role==RosterRole.Cpu?new Color("81a5ff"):new Color("a7aaa4"));
            card.Appearance.Text=AppearanceNames[slot.Appearance]+"  →";
            card.Binding.Text=slot.Role==RosterRole.Human?ControllerHub.Current.BindingLabel(human-1,Roster.Humans):Roster.Binding(i,pads);
            card.Binding.ClipText=true;card.Binding.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            card.Binding.TooltipText=card.Binding.Text;
            card.Appearance.Modulate=slot.Role==RosterRole.Off?new Color(.65f,.65f,.65f):Colors.White;
        }
        _summary.Text=$"{Roster.Humans} HUMAN  +  {Roster.CpuPlayers} CPU    /    {Roster.Participants} CONTENDERS     •     {pads} GAMEPADS CONNECTED";
        _notice.Text="MAN  Human    COM  Computer    OFF  Empty seat";
        _updating=false;Select(_selected);
    }
    private static void StripControl(Button control)
    {
        foreach(var state in new[]{"normal","hover","pressed","focus"})
        {
            var fill=state=="hover"?new Color(1,1,1,.35f):state=="pressed"?new Color(ArcadeTheme.Gold,.4f):new Color(1,1,1,0);
            var style=ArcadeTheme.Panel(fill,state=="focus"?ArcadeTheme.Red:new Color(ArcadeTheme.Ink,.22f),0,0,5);
            style.BorderWidthBottom=state=="focus"?3:state=="normal"?1:2;
            control.AddThemeStyleboxOverride(state,style);
        }
    }
    public void FocusFirst()=>_cards[0].Role.GrabFocus();
}


/// <summary>Selection brackets sit beside the character rather than replacing a real focus control.</summary>
public partial class RosterSeatPanel : PanelContainer
{
    public bool Selected {get;set;}
    public override void _Draw()
    {
        if(!Selected)return;
        float y=Size.Y*.43f;
        foreach(float edge in new[]{0f,Size.X})
        {
            float direction=edge==0?1:-1;
            Vector2[] triangle=[new(edge+direction*4,y-15),new(edge+direction*4,y+15),new(edge+direction*23,y)];
            DrawColoredPolygon(triangle,ArcadeTheme.Red);
            DrawPolyline([triangle[0],triangle[1],triangle[2],triangle[0]],new Color("ffb18b"),2,true);
        }
    }
}

public readonly record struct LineupPose(double Clock,float WalkPose,float Bob,int Appearance,bool Visible);
