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
    private readonly List<(PanelContainer Panel,OptionButton Role,Button Appearance,Label Title,Label Binding,CharacterPreview Preview)> _cards=[];
    private Label _summary=null!,_notice=null!;
    private int _selected;
    private bool _updating;
    public static readonly string[] AppearanceNames=["WHITE","BLACK","RED","BLUE","GREEN","GOLD","PURPLE","CYAN"];
    public object DiagnosticState=>new {humans=Roster.Humans,cpu=Roster.CpuPlayers,selected=_selected,slots=Roster.Slots.Select((s,i)=>new {slot=i,role=s.Role.ToString().ToLowerInvariant(),appearance=s.Appearance,binding=Binding(i),selected=i==_selected,animationTime=_cards[i].Preview.AnimationTime,walkPose=_cards[i].Preview.WalkPose,previewBob=_cards[i].Preview.BobHeight,previewAppearance=_cards[i].Preview.Appearance}).ToArray()};
    private string Binding(int slot)=>Roster.Slots[slot].Role==RosterRole.Human
        ?ControllerHub.Current.BindingLabel(Roster.Slots.Take(slot).Count(s=>s.Role==RosterRole.Human),Roster.Humans)
        :Roster.Binding(slot,ControllerHub.Current.PlayableCount);
    public override void _Ready()
    {
        AddThemeConstantOverride("separation",10);
        var header=ArcadeTheme.Row(this,20);ArcadeTheme.Text(header,"PLAYER SELECT",34).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Text(header,"01  PLAYERS   /   02  RULES   /   03  BATTLE",15);
        ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke()).Name="RosterBack";
        _summary=ArcadeTheme.Text(this,"",17);
        var grid=new GridContainer {Columns=8,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddThemeConstantOverride("h_separation",3);grid.AddThemeConstantOverride("v_separation",12);AddChild(grid);
        for(int i=0;i<8;i++)
        {
            int slot=i;var panel=new PanelContainer {CustomMinimumSize=new(150,460),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};grid.AddChild(panel);
            var column=ArcadeTheme.Column(panel,8);
            var title=ArcadeTheme.Text(column,"",15);title.HorizontalAlignment=HorizontalAlignment.Center;
            var preview=new CharacterPreview {CustomMinimumSize=new(140,270),SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddChild(preview);
            var row=ArcadeTheme.Column(column,4);
            var role=new OptionButton {Name=$"Slot{slot}Role",CustomMinimumSize=new(0,40),SizeFlagsHorizontal=SizeFlags.ExpandFill};role.AddItem("OFF");role.AddItem("HUMAN");role.AddItem("CPU");row.AddChild(role);
            var appearance=new Button {Name=$"Slot{slot}Appearance",CustomMinimumSize=new(0,38),SizeFlagsHorizontal=SizeFlags.ExpandFill};appearance.AddThemeFontSizeOverride("font_size",12);row.AddChild(appearance);
            StripControl(role);StripControl(appearance);appearance.TooltipText="Cycle suit color";
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
        _selected=slot;
        for(int i=0;i<_cards.Count;i++)
        {
            var card=_cards[i];card.Preview.Selected=i==slot;
            var color=ArcadeTheme.Players[Roster.Slots[i].Appearance];
            var style=ArcadeTheme.Panel(new Color(color,i==slot?.22f:.08f),i==slot?ArcadeTheme.Red:new Color(color,.5f),0,0,8);style.BorderWidthBottom=i==slot?8:3;style.ShadowSize=0;
            card.Panel.AddThemeStyleboxOverride("panel",style);
            card.Title.AddThemeColorOverride("font_color",i==slot?ArcadeTheme.Red:ArcadeTheme.Ink);
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
            card.Title.Text=slot.Role switch {RosterRole.Human=>$"HUMAN {++human}",RosterRole.Cpu=>$"CPU {++cpu}",_=>$"SEAT {i+1}  /  OFF"};
            card.Appearance.Text=AppearanceNames[slot.Appearance]+"  →";
            card.Binding.Text=slot.Role==RosterRole.Human?ControllerHub.Current.BindingLabel(human-1,Roster.Humans):Roster.Binding(i,pads);
            card.Binding.ClipText=true;card.Binding.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            card.Binding.TooltipText=card.Binding.Text;
            card.Preview.EnabledSeat=slot.Role!=RosterRole.Off;
            if(card.Preview.Appearance!=slot.Appearance)card.Preview.SetAppearance(slot.Appearance);
            card.Appearance.Modulate=slot.Role==RosterRole.Off?new Color(.65f,.65f,.65f):Colors.White;
        }
        _summary.Text=$"{Roster.Humans} HUMAN  +  {Roster.CpuPlayers} CPU    /    {Roster.Participants} CONTENDERS     •     {pads} GAMEPADS CONNECTED";
        _notice.Text="Choose HUMAN, CPU or OFF. Change a suit color; every contender has the same abilities.";
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
