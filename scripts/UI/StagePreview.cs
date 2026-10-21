using Godot;
namespace BlastCircuit.UI;

public partial class StagePreview : Control
{
    public int StageIndex { get; set; }
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Draw()
    {
        var ink=ArcadeTheme.Ink;var frame=ArcadeTheme.Panel(new Color("8fd9da"),ink,3,16);frame.ShadowColor=ink;frame.ShadowSize=1;frame.ShadowOffset=new(0,5);DrawStyleBox(frame,new Rect2(Vector2.Zero,Size));
        var center=new Vector2(Size.X*.5f,Size.Y*.51f);
        const float scale=17;
        for(int z=0;z<11;z++)for(int x=0;x<13;x++)
        {
            int tier=StageIndex==0?1+(Math.Abs(x-6)>3?2:z<3?3:0):StageIndex==2?x<4?0:x>8?2:1:0;
            var p=center+new Vector2((x-z)*scale,(x+z-11)*scale*.41f-tier*7);
            var diamond=new[]{p+new Vector2(0,-7),p+new Vector2(17,0),p+new Vector2(0,7),p+new Vector2(-17,0)};
            var color=(x+z)%2==0?new Color("f9df9d"):new Color("eaca81");
            if(StageIndex==0&&x>3&&x<9&&z>3&&z<8)color=new Color("2d9caf");
            DrawColoredPolygon(diamond,color);DrawPolyline([diamond[0],diamond[1],diamond[2],diamond[3],diamond[0]],new Color("b39253"),1,true);
            if(x%3==0&&z%3==0&&x>0&&z>0)
            {DrawRect(new Rect2(p-new Vector2(7,23),new Vector2(14,23)),new Color("d1b879"));DrawRect(new Rect2(p-new Vector2(9,26),new Vector2(18,5)),new Color("fff0b6"));}
        }
        string name=StageIndex==0?"Sunken Temple":StageIndex==1?"Rock Garden":"Up & Down",detail=StageIndex==0?"4 levels":StageIndex==1?"Open arena":"3 levels";
        var at=new Vector2(20,Size.Y-18);
        DrawStringOutline(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,6,ArcadeTheme.Cream);DrawString(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,ink);
        float width=ArcadeTheme.Display.GetStringSize(name,HorizontalAlignment.Left,-1,18).X;
        DrawString(ArcadeTheme.Body,at+new Vector2(width+12,0),detail,HorizontalAlignment.Left,-1,15,new Color(ink,.75f));
    }
}
