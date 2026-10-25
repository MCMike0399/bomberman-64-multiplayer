using Godot;
using Bomber.Game.Simulation;
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
        if(StageIndex==1)DrawGarden(area);
        else if(StageIndex==2)DrawUpAndDown(area);
        else if(StageIndex==4)DrawGreedyTrap(area);
        else for(int z=0;z<11;z++)for(int x=0;x<13;x++)
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
        string name=StageIndex==0?"Sunken Temple":StageIndex==1?"Rock Garden":StageIndex==2?"Up & Down":StageIndex==3?"Top Rules":"Greedy Trap",detail=StageIndex==0?"4 levels":StageIndex==1?"Walled garden":StageIndex==2?"3 levels":StageIndex==3?"Castle bridges":"Reward pit";
        var at=new Vector2(20,Size.Y-14);
        DrawStringOutline(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,6,ArcadeTheme.Cream);DrawString(ArcadeTheme.Display,at,name,HorizontalAlignment.Left,-1,18,ink);
        float width=ArcadeTheme.Display.GetStringSize(name,HorizontalAlignment.Left,-1,18).X;
        DrawString(ArcadeTheme.Body,at+new Vector2(width+12,0),detail,HorizontalAlignment.Left,-1,15,new Color(ink,.75f));
    }
    private void DrawGreedyTrap(Rect2 area)
    {
        var layout=BlastStages.Get(BlastStages.GreedyTrap);
        float scale=Math.Min(area.Size.X/20,area.Size.Y/15);
        var origin=area.GetCenter()-new Vector2(layout.Width*scale/2,layout.Height*scale*.3f);
        for(int z=0;z<layout.Height;z++)for(int x=0;x<layout.Width;x++)
        {
            var cell=layout.At(x,z);float tier=cell.Tier;
            var point=origin+new Vector2(x*scale,z*scale*.6f-tier*scale*.5f);
            var color=cell.Kind==CellKind.Pillar?new Color("712c39"):cell.Kind==CellKind.Ramp?new Color("687772"):tier==2?new Color("b5a082"):tier==1?new Color("d4cdb3"):new Color("38868a");
            DrawRect(new Rect2(point,new(scale-.5f,scale*.6f-.5f)),color);
        }
    }
    private void DrawUpAndDown(Rect2 area)
    {
        var layout=BlastStages.Get(BlastStages.UpAndDown);
        var tiles=new List<(int X,int Z,CellDef Cell)>();
        for(int z=0;z<layout.Height;z++)for(int x=0;x<layout.Width;x++)
        {
            var cell=layout.At(x,z);
            if(cell.Below is {IsPassable:true} lower)tiles.Add((x,z,lower));
            if(cell.IsPassable)tiles.Add((x,z,cell));
        }
        Vector2 Project(float x,float z,float y)=>new(x-z,(x+z)*.45f-y*.8f);
        var points=tiles.SelectMany(t=>new[]{Project(t.X,t.Z,t.Cell.Tier),Project(t.X+1,t.Z+1,t.Cell.Kind==CellKind.Ramp?t.Cell.RampToTier:t.Cell.Tier)}).ToArray();
        var min=points.Aggregate(new Vector2(float.MaxValue,float.MaxValue),(a,b)=>a.Min(b));
        var max=points.Aggregate(new Vector2(float.MinValue,float.MinValue),(a,b)=>a.Max(b));
        float scale=Math.Min(area.Size.X/(max.X-min.X+2),area.Size.Y/(max.Y-min.Y+2));
        var offset=area.GetCenter()-(min+max)*(.5f*scale);
        foreach(var tile in tiles.OrderBy(t=>t.X+t.Z).ThenBy(t=>t.Cell.Tier))
        {
            var cell=tile.Cell;float y=cell.Kind==CellKind.Ramp?(cell.RampFromTier+cell.RampToTier)/2:cell.Tier;
            var diamond=new[]{Project(tile.X,tile.Z,y),Project(tile.X+1,tile.Z,y),Project(tile.X+1,tile.Z+1,y),Project(tile.X,tile.Z+1,y)}.Select(p=>offset+p*scale).ToArray();
            bool perimeter=tile.X is 2 or 21||tile.Z is 2 or 21;
            var color=cell.Kind==CellKind.Ramp||cell.Below!=null&&perimeter?new Color("79639c"):
                cell.Tier==4&&cell.Below==null?new Color("a7b74f"):new Color("d3ded7");
            DrawColoredPolygon(diamond,color);
            DrawPolyline([diamond[0],diamond[1],diamond[2],diamond[3],diamond[0]],new Color("637a85"),.6f,true);
        }
    }

    private void DrawGarden(Rect2 area)
    {
        var stage=BlastStages.Get(BlastStages.RockGarden);
        float scale=Math.Min(area.Size.X/(stage.Width+1),area.Size.Y/(stage.Height*.6f+3));
        Vector2 Project(float x,float z,float y=0)=>area.GetCenter()+new Vector2((x-stage.Width*.5f)*scale,(z-stage.Height*.5f)*scale*.6f-y*scale);
        for(int z=0;z<stage.Height;z++)for(int x=0;x<stage.Width;x++)
        {
            bool wall=x==0||z==0||x==stage.Width-1||z==stage.Height-1;
            bool paved=Math.Abs(x-stage.Width/2)+Math.Abs(z-stage.Height/2)<=3;
            var p=Project(x,z,wall?.7f:0);
            DrawRect(new Rect2(p,new(scale,scale*.6f)),wall?new Color("b8bca4"):paved?new Color("d0d0bb"):new Color("6a993c"));
            if(wall)DrawLine(p,p+new Vector2(scale,0),new Color("e4e3ce"),1);
            else if(stage.At(x,z).Kind==CellKind.Pillar)
            {
                var foot=Project(x+.5f,z+.5f);
                float height=(stage.At(x,z).Tier+(stage.At(x,z).ObstacleHeight??.9f))*scale;
                DrawLine(foot,foot-new Vector2(0,height),new Color("8a927e"),scale*.8f);
                DrawCircle(foot-new Vector2(0,height),scale*.42f,new Color("dbdcc4"));
            }
        }
    }
}
