using Godot;
namespace BlastCircuit.UI.Menu;

public partial class StagePreview : Control
{
    public int StageIndex { get; set; }
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;ClipContents=true;Resized+=QueueRedraw;}
    public override void _Draw()
    {
        var ink=ArcadeTheme.Ink;var frame=ArcadeTheme.Panel(new Color("8fd9da"),ink,3,16);frame.ShadowColor=ink;frame.ShadowSize=1;frame.ShadowOffset=new(0,5);DrawStyleBox(frame,new Rect2(Vector2.Zero,Size));
        // The isometric board is measured in tile units s: 13x11 tiles span x -11s..13s and y about -6.2s..5s
        // (tiers and pillars rise above the far corner). Fit that into the frame above the caption band.
        const float caption=34;
        var area=new Rect2(16,12,Size.X-32,Size.Y-12-caption);
        float s=Math.Min(area.Size.X/24.5f,area.Size.Y/11.4f),h=s*.41f;
        var center=area.GetCenter()+new Vector2(-s,.62f*s);
        for(int z=0;z<11;z++)for(int x=0;x<13;x++)
        {
            int tier=StageIndex==3?(x is 0 or 3 or 6 or 9 or 12||z is 0 or 5 or 10?2:0):StageIndex==0?1+(Math.Abs(x-6)>3?2:z<3?3:0):StageIndex==2?x<4?0:x>8?2:1:0;
            var p=center+new Vector2((x-z)*s,(x+z-11)*h-tier*h);
            var diamond=new[]{p+new Vector2(0,-h),p+new Vector2(s,0),p+new Vector2(0,h),p+new Vector2(-s,0)};
            var color=(x+z)%2==0?new Color("f9df9d"):new Color("eaca81");
            if(StageIndex==0&&x>3&&x<9&&z>3&&z<8)color=new Color("2d9caf");
            if(StageIndex==3)color=tier==2?(z==5&&x is 3 or 6 or 9?new Color("e6e4cd"):new Color("787c89")):new Color("942c3c");
            DrawColoredPolygon(diamond,color);DrawPolyline([diamond[0],diamond[1],diamond[2],diamond[3],diamond[0]],new Color("b39253"),1,true);
            if(StageIndex!=3&&x%3==0&&z%3==0&&x>0&&z>0)
            {DrawRect(new Rect2(p-new Vector2(.41f*s,1.35f*s),new Vector2(.82f*s,1.35f*s)),new Color("d1b879"));DrawRect(new Rect2(p-new Vector2(.53f*s,1.53f*s),new Vector2(1.06f*s,.3f*s)),new Color("fff0b6"));}
        }
        string name=StageIndex==0?"Sunken Temple":StageIndex==1?"Rock Garden":StageIndex==2?"Up & Down":"Top Rules",detail=StageIndex==0?"4 levels":StageIndex==1?"Open arena":StageIndex==2?"3 levels":"Castle bridges";
        var at=new Vector2(20,Size.Y-14);
        DrawStringOutline(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,6,ArcadeTheme.Cream);DrawString(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,ink);
        float width=ArcadeTheme.Display.GetStringSize(name,HorizontalAlignment.Left,-1,18).X;
        DrawString(ArcadeTheme.Body,at+new Vector2(width+12,0),detail,HorizontalAlignment.Left,-1,15,new Color(ink,.75f));
    }
}
