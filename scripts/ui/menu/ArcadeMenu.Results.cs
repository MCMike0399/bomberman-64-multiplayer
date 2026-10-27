using Godot;
using System.Text.Json;
namespace BlastCircuit.UI.Menu;

/// <summary>Results standings board, medals and winner preview.</summary>
public partial class ArcadeMenu
{
    private void BuildResults()
    {
        var screen=Screen("Results");

        _results=new VBoxContainer {Position=new(100,170),Size=new(1215,585)};screen.AddChild(_results);
        _resultTitle=ArcadeTheme.Heading(screen,"Battle Complete!",58);
        _resultTitle.Position=new(100,48);_resultTitle.Size=new(1240,80);
        _resultTitle.HorizontalAlignment=HorizontalAlignment.Center;
        _resultTitle.ClipText=true;_resultTitle.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        var body=ArcadeTheme.Row(_results,55);body.SizeFlagsVertical=SizeFlags.ExpandFill;
        var champion=ArcadeTheme.Column(body,0);champion.CustomMinimumSize=new(475,0);
        _winnerPreview=new CharacterPreview {Name="WinnerPreview",Platform=false,ResultsPresentation=true,CustomMinimumSize=new(475,440),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};champion.AddChild(_winnerPreview);
        ArcadeTheme.Text(champion,"Last one standing",19,ArcadeTheme.Gold).HorizontalAlignment=HorizontalAlignment.Center;
        // The standings are printed onto the 3D board: FitStandings keeps them inside its projected face.
        _standingsBoard=ArcadeTheme.Column(screen,6);_standingsBoard.Name="StandingsBoard";_standingsBoard.Position=new(660,200);_standingsBoard.Size=new(600,400);
        var heading=ArcadeTheme.Row(_standingsBoard);ArcadeTheme.Heading(heading,"Final standings",28).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var winsCaption=ArcadeTheme.Text(heading,"Round wins",16,ArcadeTheme.Muted);winsCaption.VerticalAlignment=VerticalAlignment.Center;winsCaption.SizeFlagsVertical=SizeFlags.Fill;
        _resultRows=ArcadeTheme.Column(_standingsBoard,4);_resultRows.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var scores=ArcadeTheme.Text(_results,"",1);scores.Name="Standings";scores.Visible=false;
        var row=ArcadeTheme.Row(_results,25);
        var replay=new ArcadeModeButton {Text="Map Select",CustomMinimumSize=new(590,88),FaceColor=Coral,Accent=ArcadeTheme.Gold,SizeFlagsHorizontal=SizeFlags.ExpandFill};replay.Pressed+=()=>ReplayRequested?.Invoke();row.AddChild(replay);_replay=replay;
        var modes=new ArcadeModeButton {Text="Mode Select",CustomMinimumSize=new(590,88),FaceColor=ArcadeTheme.Indigo,Accent=ArcadeTheme.Pink,SizeFlagsHorizontal=SizeFlags.ExpandFill};modes.Pressed+=()=>Navigation?.Invoke("Modes");row.AddChild(modes);
        RegisterNavigation("Results",[],Choices(row));
    }

    /// <summary>Places the standings inside the board's velvet face as the parallax camera moves, and sizes rows to the player count.</summary>
    private void FitStandings()
    {
        if(_background.ResultsBoardFace() is not {} face)return;
        var toScreen=_screens["Results"].GetGlobalTransform().AffineInverse()*_background.GetGlobalTransform();
        var topLeft=toScreen*face.Position;var bottomRight=toScreen*face.End;
        var inner=new Rect2(topLeft,bottomRight-topLeft).GrowIndividual(-28,-16,-30,-18);
        _standingsBoard.Position=inner.Position.Round();_standingsBoard.Size=inner.Size.Round();
        int count=Math.Max(1,_resultRows.GetChildCount());
        float row=Mathf.Floor(Math.Clamp((inner.Size.Y-46-4*(count-1))/count,26,52));
        if(Mathf.IsEqualApprox(row,_standingsRowHeight))return;
        _standingsRowHeight=row;
        foreach(var child in _resultRows.GetChildren())SizeResultRow((Control)child,row);
    }

    private static void SizeResultRow(Control row,float height)
    {
        row.CustomMinimumSize=new(0,height);
        foreach(var part in row.GetChildren().OfType<Control>())
        {
            if(part is RankBadge)part.CustomMinimumSize=new(height-6,height-6);
            else if(part is BomberPortrait)part.CustomMinimumSize=new(height-4,height);
            else if(part is ResultMedals)part.CustomMinimumSize=new(170,Math.Min(44,height));
            // Text shrinks with dense eight-player boards so the row, not its glyphs, sets the height.
            else if(part is Label label)
            {
                bool score=label.GetThemeFont("font")==ArcadeTheme.Display;
                label.AddThemeFontSizeOverride("font_size",(int)Math.Min(score?26:22,height*(score?.6f:.58f)));
            }
        }
    }

    public void ShowResults(JsonElement? state,JsonElement? result,bool party=false)
    {
        _replay.Text="Map Select";
        _replay.Disabled=party;
        var text="The battle has ended.";
        JsonElement? rows=null;
        if(result is { } final && final.TryGetProperty("details",out var details) && details.ValueKind==JsonValueKind.Object && details.TryGetProperty("scoreboard",out var scoreboard)) rows=scoreboard;
        else if(state is { } data && data.TryGetProperty("players",out var players)) rows=players;
        _winnerPreview.Visible=false;_resultTitle.Text="Battle Complete!";
        if(rows is { } scores)
        {
            var ordered=scores.EnumerateArray().OrderByDescending(p=>p.GetProperty("wins").GetInt32()).ToArray();
            foreach(var child in _resultRows.GetChildren()){_resultRows.RemoveChild(child);child.QueueFree();}
            _background.SetResultCount(ordered.Length);_standingsRowHeight=0;
            foreach(var (player,rank) in ordered.Select((p,i)=>(p,i)))AddResultRow(player,rank);
            text=string.Join("\n",ordered.Select((p,i)=>$"{i+1:00}    {p.GetProperty("name").GetString()}     {p.GetProperty("wins").GetInt32()} "+(p.GetProperty("wins").GetInt32()==1?"WIN":"WINS")));
            if(ordered.Length>0&&result is {} completed&&completed.TryGetProperty("reason",out var won)&&won.GetString() is "won" or "bot_won")
            {
                var leader=ordered[0];int appearance=leader.TryGetProperty("appearance",out var suit)?suit.GetInt32():leader.GetProperty("slot").GetInt32();
                _winnerPreview.SetAppearance(appearance);_winnerPreview.Celebrate();_winnerPreview.Visible=true;
                _resultTitle.Text=(leader.GetProperty("name").GetString()??"Player")+" Wins!";
            }
        }
        if(result is { } r && r.TryGetProperty("reason",out var reason)) text+="\n\n"+(reason.GetString() switch {"bot_won"=>"A CPU rival takes the match!","won"=>"Thanks for playing!",_=>reason.GetString()});
        _results.GetNode<Label>("Standings").Text=text;ShowScreen("Results");
    }

    private void AddResultRow(JsonElement player,int rank)
    {
        int slot=player.GetProperty("slot").GetInt32();
        int appearance=player.TryGetProperty("appearance",out var suit)?suit.GetInt32():slot;
        var row=ArcadeTheme.Row(_resultRows,12);
        row.AddChild(new RankBadge {Rank=rank+1,SizeFlagsVertical=SizeFlags.ShrinkCenter});
        var character=new BomberPortrait {Suit=ArcadeTheme.Players[Math.Clamp(appearance,0,7)],SizeFlagsVertical=SizeFlags.ShrinkCenter};row.AddChild(character);
        var name=ArcadeTheme.Text(row,player.GetProperty("name").GetString()??"Player",22,rank==0?ArcadeTheme.Gold:ArcadeTheme.Cream);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.VerticalAlignment=VerticalAlignment.Center;
        name.ClipText=true;name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        int wins=player.GetProperty("wins").GetInt32();
        row.AddChild(new ResultMedals {Wins=wins,SizeFlagsVertical=SizeFlags.ShrinkCenter});
        // A zero recedes so the eye lands on actual round wins.
        var count=ArcadeTheme.Text(row,wins.ToString(),26,wins>0?ArcadeTheme.Gold:ArcadeTheme.Muted,true);count.CustomMinimumSize=new(38,0);count.HorizontalAlignment=HorizontalAlignment.Right;count.VerticalAlignment=VerticalAlignment.Center;
        SizeResultRow(row,_standingsRowHeight>0?_standingsRowHeight:48);
    }
}
