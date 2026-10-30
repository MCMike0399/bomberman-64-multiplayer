using Godot;
namespace BlastCircuit.UI.Menu;

/// <summary>Explicit column routes keep native GUI focus independent of decorative geometry.</summary>
public partial class ArcadeMenu
{
    private sealed record FocusLayout(Control[] Header,Control[] Footer,Control[][] Columns);

    public bool HasOpenPopup=>_screen=="Roster"&&_localPlayers.HasOpenPopup;
    public bool OwnsPopupInput=>_screen=="Roster"&&_localPlayers.OwnsPopupInput;

    private static bool CanFocus(Control? control)=>control is not null&&GodotObject.IsInstanceValid(control)
        &&control.IsVisibleInTree()&&control.FocusMode==FocusModeEnum.All&&control is not BaseButton {Disabled:true};

    private static Control[] Choices(Node node)
    {
        var result=new List<Control>();
        foreach(var child in node.GetChildren())
        {
            if(child is BaseButton or LineEdit or Slider)result.Add((Control)child);
            else result.AddRange(Choices(child));
        }
        return result.ToArray();
    }

    private void RegisterNavigation(string screen,Control[] header,Control[] footer,params Control[][] columns)
    {
        _focusLayouts[screen]=new(header,footer,columns);
        foreach(var control in header.Concat(columns.SelectMany(c=>c)).Concat(footer))
            control.FocusEntered+=()=>
            {
                if(_screen!=screen)return;
                _savedFocus[_page]=control;
                for(int i=0;i<columns.Length;i++)if(columns[i].Contains(control))
                {_activeColumn[screen]=i;_columnFocus[(screen,i)]=control;break;}
                RefreshNavigation();
            };
    }

    private void RefreshNavigation()
    {
        if(!_focusLayouts.TryGetValue(_screen,out var layout))return;
        var header=layout.Header.Where(CanFocus).ToArray();
        var footer=layout.Footer.Where(CanFocus).ToArray();
        var columns=layout.Columns.Select(c=>c.Where(CanFocus).ToArray()).ToArray();
        var order=header.Concat(columns.SelectMany(c=>c)).Concat(footer).ToArray();
        if(order.Length==0)return;
        // Tab follows reading order, including text fields and sliders. It never enters hidden state nodes.
        for(int i=0;i<order.Length;i++)
        {
            var control=order[i];
            control.FocusNext=control.GetPathTo(order[(i+1)%order.Length]);
            control.FocusPrevious=control.GetPathTo(order[(i+order.Length-1)%order.Length]);
        }
        int active=_activeColumn.GetValueOrDefault(_screen);
        if(active>=columns.Length||columns[active].Length==0)active=Array.FindIndex(columns,c=>c.Length>0);
        Control? top=active>=0?columns[active].FirstOrDefault():null;
        Control? bottom=active>=0?columns[active].LastOrDefault():null;
        Control Entry(int column,int row)
        {
            // Directional grid movement preserves rows; only shoulder switches recall an old selection.
            if(_screen=="Tutorial")return column==0?_lessonButtons[_selectedLesson.Id]:_lessonStart;
            return columns[column][Math.Min(row,columns[column].Length-1)];
        }
        for(int c=0;c<columns.Length;c++)for(int row=0;row<columns[c].Length;row++)
        {
            var control=columns[c][row];
            int left=c-1,right=c+1;
            while(left>=0&&columns[left].Length==0)left--;
            while(right<columns.Length&&columns[right].Length==0)right++;
            Neighbors(control,
                row>0?columns[c][row-1]:header.LastOrDefault()??control,
                row+1<columns[c].Length?columns[c][row+1]:footer.FirstOrDefault()??control,
                left>=0?Entry(left,row):control,right<columns.Length?Entry(right,row):control);
        }
        void Horizontal(Control[] controls,bool isHeader)
        {
            for(int i=0;i<controls.Length;i++)Neighbors(controls[i],isHeader?controls[i]:bottom??header.LastOrDefault()??controls[i],
                isHeader?top??footer.FirstOrDefault()??controls[i]:controls[i],
                controls[Math.Max(0,i-1)],controls[Math.Min(controls.Length-1,i+1)]);
        }
        Horizontal(header,true);Horizontal(footer,false);
    }

    private static void Neighbors(Control control,Control up,Control down,Control left,Control right)
    {
        control.FocusNeighborTop=control.GetPathTo(up);control.FocusNeighborBottom=control.GetPathTo(down);
        control.FocusNeighborLeft=control.GetPathTo(left);control.FocusNeighborRight=control.GetPathTo(right);
    }

    /// <summary>Shoulders switch columns even when left/right edits a dial or a text caret.</summary>
    public bool SwitchColumn(int direction)
    {
        if(!_focusLayouts.TryGetValue(_screen,out var layout))return false;
        var columns=layout.Columns.Select((controls,index)=>(controls:controls.Where(CanFocus).ToArray(),index))
            .Where(c=>c.controls.Length>0).ToArray();
        if(columns.Length<2)return false;
        var focus=GetViewport().GuiGetFocusOwner();
        int current=Array.FindIndex(columns,c=>c.controls.Contains(focus));
        if(current<0)current=Array.FindIndex(columns,c=>c.index==_activeColumn.GetValueOrDefault(_screen));
        int next=(Math.Max(0,current)+direction+columns.Length)%columns.Length;
        var column=columns[next];
        var target=_columnFocus.GetValueOrDefault((_screen,column.index));
        (target is not null&&column.controls.Contains(target)?target:column.controls[0]).GrabFocus();
        return true;
    }

    private void FocusScreen(bool restore)
    {
        RefreshNavigation();
        Control? target=restore?_savedFocus.GetValueOrDefault(_page):null;
        if(!CanFocus(target))target=DefaultFocus(_screen);
        if(!CanFocus(target))target=Choices(_screens[_screen]).FirstOrDefault(CanFocus);
        target?.GrabFocus();
    }

    public void FocusStart()
    {
        Control? target=_screen switch
        {
            "Roster"=>_screens["Roster"].FindChild("RosterContinue",true,false) as Control,
            "Setup"=>_launch,
            "Advanced"=>_screens["Advanced"].FindChild("AdvancedBack",true,false) as Control,
            "Tutorial"=>_lessonStart,
            "Online"=>OnlinePrimary(),
            "Lobby"=>CanFocus(_startRoom)?_startRoom:CanFocus(_copyCode)?_copyCode:DefaultFocus("Lobby"),
            "Settings"=>_screens["Settings"].FindChild("SettingsBack",true,false) as Control,
            "Pause"=>_screens["Pause"].FindChild("ResumeGame",true,false) as Control,
            "Results"=>_replay,
            _=>DefaultFocus(_screen)??Choices(_screens[_screen]).FirstOrDefault(CanFocus)
        };
        if(CanFocus(target))target!.GrabFocus();
    }

    private Control? OnlinePrimary()
    {
        if(_screens["Online"].FindChild("CancelConnect",true,false) is Control cancel&&CanFocus(cancel))return cancel;
        var focus=GetViewport().GuiGetFocusOwner();
        if(_focusLayouts["Online"].Columns[1].Contains(focus))return _onlineButtons[0];
        if(focus==_code||focus==_onlineButtons[1])return _onlineButtons[1];
        return _onlineButtons[2];
    }
}
