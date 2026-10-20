using Godot;
namespace BlastCircuit.UI;

/// <summary>Shared arcade typography, colors and tactile control styles.</summary>
public static class ArcadeTheme
{
    public static readonly Color Ink = new("193560"), Cream = new("fff6dc"), Gold = new("ffcf52"), Red = new("ed4b55"), Pink = new("f184b1"), Aqua = new("71d9da");
    public static readonly Color[] Players = [new("f8f6ed"),new("354052"),new("ed5665"),new("7296ee"),new("59bf8c"),new("f0c53d"),new("d581d8"),new("62d4dc")];
    public static Theme Create()
    {
        var t = new Theme { DefaultFontSize = 20 };
        // Bundled Godot fallback stays portable across exported platforms.
        t.SetColor("font_color", "Label", Ink);
        foreach(var type in new[]{"Button","OptionButton"})
        {
            t.SetColor("font_color",type,Ink);t.SetColor("font_hover_color",type,Ink);t.SetColor("font_pressed_color",type,Ink);
            t.SetColor("font_focus_color",type,Ink);t.SetColor("font_hover_pressed_color",type,Ink);t.SetColor("font_disabled_color",type,new Color("697486"));
            t.SetStylebox("normal",type,Panel(Colors.White,Ink,3));t.SetStylebox("hover",type,Panel(Gold.Lightened(.35f),Ink,3));
            t.SetStylebox("pressed",type,Panel(Gold,Ink,3));t.SetStylebox("focus",type,Panel(new Color(0,0,0,0),Pink,4));
            t.SetStylebox("disabled",type,Panel(new Color("e1dfd7"),new Color("a3a8ab"),2));
        }
        t.SetStylebox("normal","LineEdit",Panel(Colors.White,Ink,2));t.SetStylebox("focus","LineEdit",Panel(Cream,Ink,3));t.SetColor("font_color","LineEdit",Ink);t.SetColor("font_placeholder_color","LineEdit",new("768299"));t.SetColor("caret_color","LineEdit",Ink);
        t.SetColor("font_color","PopupMenu",Ink);t.SetStylebox("panel","PopupMenu",Panel(Cream,Ink,3));t.SetStylebox("hover","PopupMenu",Panel(Gold,Ink,0));
        t.SetColor("font_color","CheckButton",Ink);t.SetColor("font_focus_color","CheckButton",Ink);t.SetColor("font_color","SpinBox",Ink);
        return t;
    }
    public static StyleBoxFlat Panel(Color fill,Color? edge=null,int border=3,int radius=16,int margin=16)=>new()
    {
        BgColor=fill,BorderColor=edge??Ink,BorderWidthTop=border,BorderWidthBottom=border,BorderWidthLeft=border,BorderWidthRight=border,
        CornerRadiusTopLeft=radius,CornerRadiusTopRight=radius,CornerRadiusBottomLeft=radius,CornerRadiusBottomRight=radius,
        ContentMarginLeft=margin,ContentMarginRight=margin,ContentMarginTop=margin,ContentMarginBottom=margin,
        ShadowColor=new Color(Ink,.13f),ShadowSize=4,ShadowOffset=new Vector2(0,5)
    };
    public static Label Text(Node parent,string text,int size=20,Color? color=null)
    {
        var label=new Label {Text=text};label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color??Ink);parent.AddChild(label);return label;
    }
    public static Button Button(Node parent,string text,Action action,bool primary=false)
    {
        var button=new Button {Text=text,CustomMinimumSize=new(0,52)};
        if(primary)
        {
            button.AddThemeStyleboxOverride("normal",Panel(Red));button.AddThemeStyleboxOverride("hover",Panel(Red.Lightened(.15f)));button.AddThemeStyleboxOverride("pressed",Panel(Red.Darkened(.1f)));
            button.AddThemeColorOverride("font_color",Colors.White);button.AddThemeColorOverride("font_hover_color",Colors.White);button.AddThemeColorOverride("font_pressed_color",Colors.White);
            button.AddThemeColorOverride("font_focus_color",Colors.White);button.AddThemeColorOverride("font_hover_pressed_color",Colors.White);
        }
        button.Pressed+=action;parent.AddChild(button);return button;
    }
    public static LineEdit Field(Node parent,string title,string value,string placeholder="")
    {
        Text(parent,title,15);var field=new LineEdit {Text=value,PlaceholderText=placeholder,CustomMinimumSize=new(0,48)};parent.AddChild(field);return field;
    }
    public static VBoxContainer Column(Node parent,int gap=12)
    {var box=new VBoxContainer();box.AddThemeConstantOverride("separation",gap);parent.AddChild(box);return box;}
    public static HBoxContainer Row(Node parent,int gap=12)
    {var box=new HBoxContainer();box.AddThemeConstantOverride("separation",gap);parent.AddChild(box);return box;}
}
