using Godot;
namespace BlastCircuit.UI;

/// <summary>Input prompts drawn as physical keycaps followed by a plain verb.</summary>
/// <remarks>Parses the device profile format "KEY / KEY  ACTION   KEY  ACTION".</remarks>
public partial class PromptBar : HBoxContainer
{
    private string _source="";
    public int KeySize {get;set;}=14;
    private static readonly Dictionary<string,string> Verbs=new()
    {
        ["SELECT"]="Move",["CONFIRM"]="Confirm",["BACK"]="Back",["SCREEN"]="Fullscreen",["BOMB / KICK"]="Bomb / kick",["CARRY"]="Carry",["REMOTE"]="Detonate"
    };
    private static readonly Dictionary<string,string> Keys=new()
    {
        ["SPACE"]="Space",["SHIFT"]="Shift",["ENTER"]="Enter",["ESC"]="Esc",["CTRL"]="Ctrl",["BACKSPACE"]="Backspace",["ARROWS"]="Arrows",["STICK"]="Stick",["D-PAD"]="D-pad"
    };
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;AddThemeConstantOverride("separation",6);}
    public void SetPrompt(string source)
    {
        if(source==_source)return;_source=source;
        foreach(var child in GetChildren()){RemoveChild(child);child.QueueFree();}
        foreach(var pair in source.Split("   ",StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            int split=pair.IndexOf("  ",StringComparison.Ordinal);if(split<0)continue;
            string keys=pair[..split].Trim(),verb=pair[(split+2)..].Trim();
            var cluster=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};cluster.AddThemeConstantOverride("separation",4);AddChild(cluster);
            var parts=keys.Split(" / ");
            for(int i=0;i<parts.Length;i++)
            {
                if(i>0)ArcadeTheme.Text(cluster,"/",KeySize,ArcadeTheme.Muted).VerticalAlignment=VerticalAlignment.Center;
                cluster.AddChild(Keycap(KeyName(parts[i]),KeySize));
            }
            var label=ArcadeTheme.Text(cluster,Verbs.TryGetValue(verb,out var plain)?plain:ArcadeTheme.Word(verb),KeySize+1);label.VerticalAlignment=VerticalAlignment.Center;
            cluster.AddChild(new Control {CustomMinimumSize=new(12,0),MouseFilter=MouseFilterEnum.Ignore});
        }
    }
    public static string KeyName(string key)=>Keys.TryGetValue(key,out var word)?word:key;
    /// <summary>"J / SPACE" as running text: "J or Space".</summary>
    public static string Spoken(string keys)=>string.Join(" or ",keys.Split(" / ").Select(KeyName));
    public static PanelContainer Keycap(string text,int size=14)
    {
        var cap=new PanelContainer {MouseFilter=MouseFilterEnum.Ignore,SizeFlagsVertical=SizeFlags.ShrinkCenter};
        var style=ArcadeTheme.Panel(ArcadeTheme.Cream,ArcadeTheme.Ink,2,6,0);
        style.ContentMarginLeft=style.ContentMarginRight=size*.5f;style.ContentMarginTop=1;style.ContentMarginBottom=2;
        style.ShadowColor=ArcadeTheme.Ink;style.ShadowSize=1;style.ShadowOffset=new(0,3);
        cap.AddThemeStyleboxOverride("panel",style);
        var label=new Label {Text=text,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
        label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",ArcadeTheme.Ink);label.AddThemeConstantOverride("outline_size",0);
        cap.AddChild(label);return cap;
    }
}
