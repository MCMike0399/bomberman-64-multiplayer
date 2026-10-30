using Godot;
using System.Text.Json;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

/// <summary>Visits dense menu pages and proves that copy, fields and standings stay inside their own boxes.</summary>
/// <remarks>Results use a presentation-only scoreboard fixture to stress eight long names; the separate
/// <c>results</c> journey covers the real match-to-Results path.</remarks>
public partial class PlaytestDriver
{
    private async Task UiLayout()
    {
        var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
        _root.Navigate("Online");await Frames(4);
        var server=(LineEdit)_root.FindChild("ServerUrl",true,false)!;string original=server.Text;
        server.Text="http://192.168.50.89:18496";await Frames(2);
        Check(GetViewport().GuiGetFocusOwner()?.Name=="OnlineAppearance","online page starts on the suit selector, not Back");
        AuditCards("online");
        await Capture("ui-online");
        server.Text=original;
        _root.Navigate("Setup");await Frames(3);await ActivateNamed("AdvancedSettings");
        var big=(CheckButton)_root.FindChild("Allow_big",true,false)!;big.ButtonPressed=false;await Frames(12);
        AuditCards("advanced");
        await Capture("ui-advanced");
        big.ButtonPressed=true;await ActivateNamed("AdvancedBack");
        _root.Navigate("Settings");await Frames(4);AuditCards("settings");await Capture("ui-settings");
        // Presentation fixture: exercise the two update actions alongside the display controls.
        _root.SetProcess(false);
        try
        {
            menu.UpdateStatus("0.32.0","Ready to install",busy:false,ready:true,canInstall:true);
            await Frames(4);AuditCards("settings-update-ready");await Capture("ui-settings-update-ready");
        }
        finally {_root.SetProcess(true);}
        _root.Navigate("Modes");await Frames(3);
        // A full eight-seat room fixture: long names and a reconnecting seat at the widest status text.
        string[] seats=["Maximiliano Supreme","P2","Wobbly Bomber Deluxe","P4","Explosive Grandma","P6","Captain Kaboom","P8"];
        var lobby=JsonSerializer.SerializeToElement(new {state="open",joinCode="7F449R",players=seats.Select((name,i)=>new {playerId="seat"+i,displayName=name,slot=i,connected=i!=5})});
        menu.ShowScreen("Lobby");menu.ShowLobby(lobby,"7F449R","Connected");await Frames(12);
        AuditCards("lobby");
        await Capture("ui-lobby-8");
        menu.ResetLobby();_root.Navigate("Modes");await Frames(3);
        foreach(var names in new[]{new[]{"Bot 2","P1","Bot 1","Bot 3"},new[]{"Maximiliano Supreme","Bot 1","Wobbly Bomber Deluxe","Bot 3","P2","Bot 5","Explosive Grandma","Bot 7"}})
        {
            var rows=names.Select((name,i)=>new {name,slot=i,appearance=(i*3+2)%8,wins=Math.Max(0,3-i)}).ToArray();
            var result=JsonSerializer.SerializeToElement(new {reason="bot_won",details=new {scoreboard=rows}});
            menu.ShowResults(null,result);await Frames(20);
            AuditStandings(names.Length);
            await Capture($"ui-results-{names.Length}");
        }
        _root.Navigate("Modes");await Frames(3);
    }
    /// <summary>Every painted control lies inside its card (respecting real scroll clipping), and single-line fields show all their text.</summary>
    private void AuditCards(string page)
    {
        int audited=0;var problems=new List<string>();
        foreach(var control in AuditDescendants(_root).OfType<Control>().Where(c=>c.IsVisibleInTree()&&c.Size.X>0))
        {
            var card=Ancestors(control).OfType<PanelContainer>().FirstOrDefault();
            if(card==null)continue;
            audited++;
            var painted=control.GetGlobalRect();
            foreach(var scroll in Ancestors(control).OfType<ScrollContainer>())painted=painted.Intersection(scroll.GetGlobalRect());
            if(painted.HasArea()&&!card.GetGlobalRect().Grow(1).Encloses(painted))problems.Add($"{control.GetPath()} {painted} outside {card.GetGlobalRect()}");
            if(control is LineEdit field)
            {
                var font=field.GetThemeFont("font");int size=field.GetThemeFontSize("font_size");var style=field.GetThemeStylebox("normal");
                float room=field.Size.X-style.GetMargin(Side.Left)-style.GetMargin(Side.Right);
                if(font.GetStringSize(field.Text,HorizontalAlignment.Left,-1,size).X>room)problems.Add($"{field.Name} text '{field.Text}' wider than {room}");
            }
            if(control is Label {ClipText:false,AutowrapMode:TextServer.AutowrapMode.Off} label&&label.GetMinimumSize().X>label.Size.X+1)
                problems.Add($"{label.GetPath()} '{label.Text}' wider than its rect");
        }
        Event("layout-audit",new {page,audited,problems});
        Check(audited>10&&problems.Count==0,$"{page} controls stay inside their cards ({audited} audited): "+string.Join("; ",problems.Take(4)));
    }
    private void AuditStandings(int players)
    {
        var board=(Control)_root.FindChild("StandingsBoard",true,false)!;
        var backdrop=AuditDescendants(_root).OfType<ArcadeBackdrop>().Single();
        Check(backdrop.ResultsBoardFace() is {} face&&backdrop.GetGlobalTransform()*face.Position is var start&&new Rect2(start,face.Size*backdrop.GetGlobalTransform().Scale).Grow(1).Encloses(board.GetGlobalRect()),
            $"{players}-player standings sit inside the projected 3D board face");
        var rows=board.FindChildren("*","HBoxContainer",true,false).OfType<Control>().Where(r=>r.IsVisibleInTree()).ToArray();
        Event("standings-layout",new {players,face=backdrop.ResultsBoardFace()?.ToString(),board=board.GetGlobalRect().ToString(),rows=rows.Select(r=>r.GetGlobalRect().ToString()).ToArray()});
        var outside=rows.Where(r=>!board.GetGlobalRect().Grow(1).Encloses(r.GetGlobalRect())).Select(r=>r.GetGlobalRect().ToString()).ToArray();
        Check(rows.Length==players+1&&outside.Length==0,$"{players} standings rows and heading fit the board: "+string.Join("; ",outside));
    }
    private static IEnumerable<Node> Ancestors(Node node){for(var parent=node.GetParent();parent!=null;parent=parent.GetParent())yield return parent;}
}
