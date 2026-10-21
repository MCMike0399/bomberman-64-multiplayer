using Godot;
namespace BlastCircuit.UI;

/// <summary>Shared arcade typography, colors and tactile control styles.</summary>
/// <remarks>
/// Controls read as stamped temple keys: an ink outline, a solid ink lip below the face,
/// and a face that drops onto the lip when pressed. Dela Gothic One carries titles, values
/// and buttons; M PLUS Rounded 1c carries labels and running text.
/// </remarks>
public static class ArcadeTheme
{
    public static readonly Color Ink=new("193560"), Night=new("1d1a3f"), Cream=new("fff6dc"), Sand=new("f2d48c"), Gold=new("ffcf52"),
        Red=new("ef5a4f"), Pink=new("f184b1"), Aqua=new("4cc3c8"), Indigo=new("3d5aa8"), Muted=new("aab2d8");
    public static readonly Color[] Players = [new("f8f6ed"),new("354052"),new("ed5665"),new("7296ee"),new("59bf8c"),new("f0c53d"),new("d581d8"),new("62d4dc")];
    private static Font? _display,_body;
    public static Font Display=>_display??=LoadFont("res://assets/fonts/DelaGothicOne-Regular.ttf");
    public static Font Body=>_body??=LoadFont("res://assets/fonts/MPLUSRounded1c-ExtraBold.ttf");
    private static Font LoadFont(string path)=>ResourceLoader.Exists(path)&&GD.Load<FontFile>(path) is {} font?font:ThemeDB.FallbackFont;
    public static Theme Create()
    {
        var t=new Theme {DefaultFont=Body,DefaultFontSize=20};
        t.SetColor("font_color","Label",Cream);t.SetColor("font_outline_color","Label",Ink);
        foreach(var type in new[]{"Button","OptionButton"})
        {
            t.SetFont("font",type,Display);t.SetFontSize("font_size",type,19);
            t.SetColor("font_color",type,Ink);t.SetColor("font_hover_color",type,Ink);t.SetColor("font_pressed_color",type,Ink);
            t.SetColor("font_focus_color",type,Ink);t.SetColor("font_hover_pressed_color",type,Ink);t.SetColor("font_disabled_color",type,new Color(Ink,.55f));
            t.SetColor("font_outline_color",type,Ink);
            t.SetStylebox("normal",type,Key(Sand));t.SetStylebox("hover",type,Key(Sand.Lightened(.28f)));
            t.SetStylebox("pressed",type,Key(Gold,true));t.SetStylebox("hover_pressed",type,Key(Gold,true));
            t.SetStylebox("focus",type,FocusRing());t.SetStylebox("disabled",type,Key(new Color("8f93b4"),flat:true));
        }
        t.SetStylebox("normal","LineEdit",Panel(Cream,Ink,3,10,14));t.SetStylebox("focus","LineEdit",FocusRing(10));
        t.SetColor("font_color","LineEdit",Ink);t.SetColor("font_placeholder_color","LineEdit",new("8a8fa8"));t.SetColor("caret_color","LineEdit",Red);
        t.SetColor("selection_color","LineEdit",new Color(Gold,.6f));
        t.SetFont("font","PopupMenu",Display);t.SetFontSize("font_size","PopupMenu",22);
        t.SetColor("font_color","PopupMenu",Ink);t.SetColor("font_hover_color","PopupMenu",Ink);
        t.SetStylebox("panel","PopupMenu",Panel(Cream,Ink,3,12,8));t.SetStylebox("hover","PopupMenu",Panel(Gold,Ink,0,8,8));
        t.SetColor("font_color","CheckButton",Cream);t.SetColor("font_focus_color","CheckButton",Cream);t.SetColor("font_color","SpinBox",Cream);
        t.SetColor("font_color","TooltipLabel",Ink);t.SetStylebox("panel","TooltipPanel",Panel(Cream,Ink,2,8,8));
        return t;
    }
    public static StyleBoxFlat Panel(Color fill,Color? edge=null,int border=3,int radius=3,int margin=16)=>new()
    {
        BgColor=fill,BorderColor=edge??Ink,BorderWidthTop=border,BorderWidthBottom=border,BorderWidthLeft=border,BorderWidthRight=border,
        CornerRadiusTopLeft=radius,CornerRadiusTopRight=radius,CornerRadiusBottomLeft=radius,CornerRadiusBottomRight=radius,
        ContentMarginLeft=margin,ContentMarginRight=margin,ContentMarginTop=margin,ContentMarginBottom=margin,
        ShadowColor=new Color(Ink,.13f),ShadowSize=0,ShadowOffset=new Vector2(0,5)
    };
    /// <summary>A raised key: the face rides above a solid ink lip and drops onto it when pressed.</summary>
    public static StyleBoxFlat Key(Color face,bool pressed=false,bool flat=false,int radius=12,int margin=16)
    {
        var style=Panel(face,Ink,3,radius,margin);
        style.ContentMarginTop=style.ContentMarginBottom=margin*.55f;
        if(flat)return style;
        style.ShadowColor=Ink;style.ShadowSize=1;style.ShadowOffset=new(0,pressed?1:5);
        style.ExpandMarginTop=pressed?0:3;
        if(pressed)style.ContentMarginTop+=4;else style.ContentMarginBottom+=2;
        return style;
    }
    /// <summary>A gold ring drawn around whatever key currently owns keyboard or gamepad focus.</summary>
    public static StyleBoxFlat FocusRing(int radius=15)
    {
        var style=Panel(new Color(0,0,0,0),Gold,4,radius,0);
        style.ExpandMarginLeft=style.ExpandMarginRight=6;style.ExpandMarginTop=9;style.ExpandMarginBottom=10;
        return style;
    }
    /// <summary>The translucent backing plate behind dense menu content.</summary>
    public static StyleBoxFlat Plate(int margin=26)
    {
        var style=Panel(new Color(Night,.9f),new Color("394a8f"),2,20,margin);
        style.ShadowColor=new Color("0d0b22",.55f);style.ShadowSize=1;style.ShadowOffset=new(0,8);
        return style;
    }
    public static Label Text(Node parent,string text,int size=20,Color? color=null,bool display=false)
    {
        var label=new Label {Text=text};
        int outline=display?Math.Clamp(size/6,4,12):Math.Clamp(size/5,3,6);
        label.AddThemeConstantOverride("outline_size",outline);label.AddThemeColorOverride("font_outline_color",Ink);
        label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color??Cream);
        if(display)
        {
            // A hard, unblurred ink drop reads like a printed sticker over the lit 3D stage.
            label.AddThemeFontOverride("font",Display);
            label.AddThemeColorOverride("font_shadow_color",Ink);label.AddThemeConstantOverride("shadow_outline_size",outline);
            label.AddThemeConstantOverride("shadow_offset_x",0);label.AddThemeConstantOverride("shadow_offset_y",Math.Max(3,size/11));
        }
        parent.AddChild(label);return label;
    }
    public static Label Heading(Node parent,string text,int size=34,Color? color=null)=>Text(parent,text,size,color??Gold,true);
    public static Button Button(Node parent,string text,Action action,bool primary=false)
    {
        var button=new Button {Text=text,CustomMinimumSize=new(0,52),MouseDefaultCursorShape=Control.CursorShape.PointingHand};
        if(primary)
        {
            button.AddThemeStyleboxOverride("normal",Key(Red));button.AddThemeStyleboxOverride("hover",Key(Red.Lightened(.15f)));
            button.AddThemeStyleboxOverride("pressed",Key(Red.Darkened(.08f),true));button.AddThemeStyleboxOverride("hover_pressed",Key(Red.Darkened(.08f),true));
            foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"})button.AddThemeColorOverride(state,Cream);
            button.AddThemeConstantOverride("outline_size",6);
        }
        button.Pressed+=action;parent.AddChild(button);return button;
    }
    /// <summary>Text-only navigation such as Back: no key face until it is hovered or focused.</summary>
    public static Button Quiet(Button button)
    {
        foreach(var state in new[]{"normal","pressed","hover_pressed"})button.AddThemeStyleboxOverride(state,new StyleBoxEmpty {ContentMarginLeft=14,ContentMarginRight=14});
        button.AddThemeStyleboxOverride("hover",Panel(new Color(Cream,.12f),new Color(0,0,0,0),0,12,14));
        button.AddThemeStyleboxOverride("focus",FocusRing());
        foreach(var state in new[]{"font_color","font_pressed_color"})button.AddThemeColorOverride(state,Cream);
        foreach(var state in new[]{"font_hover_color","font_focus_color","font_hover_pressed_color"})button.AddThemeColorOverride(state,Gold);
        button.AddThemeConstantOverride("outline_size",5);
        return button;
    }
    public static LineEdit Field(Node parent,string title,string value,string placeholder="")
    {
        Text(parent,title,15,Muted);var field=new LineEdit {Text=value,PlaceholderText=placeholder,CustomMinimumSize=new(0,48)};parent.AddChild(field);return field;
    }
    public static VBoxContainer Column(Node parent,int gap=12)
    {var box=new VBoxContainer();box.AddThemeConstantOverride("separation",gap);parent.AddChild(box);return box;}
    public static HBoxContainer Row(Node parent,int gap=12)
    {var box=new HBoxContainer();box.AddThemeConstantOverride("separation",gap);parent.AddChild(box);return box;}
    /// <summary>"WHITE" style roster names shown as words rather than shouted labels.</summary>
    public static string Word(string upper)=>upper.Length==0?upper:upper[..1]+upper[1..].ToLowerInvariant();
}

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
