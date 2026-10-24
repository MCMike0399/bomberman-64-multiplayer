using Godot;
using Bomber.Game.Simulation;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena;

public partial class TempleStageView
{
    /// <summary>Original procedural stonework: suspended decks remain visibly open underneath.</summary>
    private void BuildUpAndDown()
    {
        _stone.SetShaderParameter("base_color",new Color("cbd5cf"));
        _stone.SetShaderParameter("mortar_color",new Color("879599"));
        _masonry.SetShaderParameter("base_color",new Color("9aa9a8"));
        _masonry.SetShaderParameter("mortar_color",new Color("596d75"));
        _bedrock.SetShaderParameter("base_color",new Color("71858b"));
        _bedrock.SetShaderParameter("mortar_color",new Color("415962"));
        _cobble.SetShaderParameter("base_color",new Color("9eae4a"));
        _cobble.SetShaderParameter("mortar_color",new Color("6b7c39"));
        var pale=_art.Color(new("e0e8db"));
        var purple=_art.Color(new("68518e"),.58f);
        var purpleLight=_art.Color(new("9f7fba"),.6f);
        var violetDark=_art.Color(new("3b3556"));
        var brass=_art.Color(new("e6cf67"),.5f);
        var stonework=new Node3D {Name="UpAndDownStonework"};AddChild(stonework);
        // Three long piers, each rooted below the water. The crown is a separate
        // thick cap above the central pier; bridges never receive these shafts.
        Pier(4,6,7,23,2); Pier(17,19,7,23,2); Pier(10,13,12,23,0); Pier(10,13,7,10,4);
        var crown=Box(stonework,new(12,4*HeightScale-.42f,9),new(4,.84f,4),_masonry,.045f);
        crown.Name="Crown";
        for(int x=0;x<Width;x++)for(int z=0;z<Depth;z++)
        {
            var cell=Layout.At(x,z);
            if(cell.Below is {} lower)
            {
                Tile(x,z,cell,true);
                if(lower.IsPassable)Tile(x,z,lower,false);
            }
            else if(cell.IsPassable)Tile(x,z,cell,false);
        }
        // Inlaid green crown, inset from the pale stone coping; no collision prop.
        Box(stonework,new(12,4*HeightScale+.006f,9),new(3.36f,.012f,3.36f),_cobble,.01f).Name="CrownMosaic";
        for(int side=-1;side<=1;side+=2)
        {
            Box(stonework,new(12+side*1.72f,4*HeightScale+.015f,9),new(.055f,.025f,3.5f),brass,.008f);
            Box(stonework,new(12,4*HeightScale+.015f,9+side*1.72f),new(3.5f,.025f,.055f),brass,.008f);
        }
        // The four dark panels distinguish the low reward deck through the high ring.
        foreach(float x in new[]{10.55f,12.45f})foreach(float z in new[]{22.25f,23.25f})
        {
            Box(stonework,new(x,.008f,z),new(.82f,.016f,.72f),violetDark,.015f);
            Box(stonework,new(x,.021f,z),new(.65f,.012f,.55f),purple,.012f);
        }
        // Recessed square friezes, kept entirely below the playable surfaces.
        foreach(var (x0,x1,z0,z1,tier) in new[]{(4,6,7,23,2),(17,19,7,23,2),(10,13,12,23,0),(10,13,7,10,4)})
        {
            float y=tier*HeightScale-.43f;
            for(int x=x0;x<=x1;x++)
            {
                Frieze(new(x+.5f,y,z1+1.016f),0);
                if(tier==4)Frieze(new(x+.5f,y,z0-.016f),Mathf.Pi);
            }
            for(int z=z0;z<=z1;z++)
            {
                Frieze(new(x0-.016f,y,z+.5f),-Mathf.Pi/2);
                Frieze(new(x1+1.016f,y,z+.5f),Mathf.Pi/2);
            }
        }

        void Pier(int x0,int x1,int z0,int z1,int tier)
        {
            float top=tier*HeightScale-.18f,bottom=-4.4f;
            float x=(x0+x1+1)/2f,z=(z0+z1+1)/2f,w=x1-x0+1,d=z1-z0+1;
            Box(stonework,new(x,(top+bottom)/2,z),new(w-.24f,top-bottom,d-.2f),_bedrock,.08f);
            Box(stonework,new(x,top-.28f,z),new(w,.4f,d),_masonry,.035f);
            for(float course=-3.6f;course<top-.6f;course+=1.6f)
                Box(stonework,new(x,course,z),new(w-.12f,.15f,d-.1f),_masonry,.018f);
            // Vertical ribs catch the light without painting the columns with noise.
            for(int row=z0;row<=z1;row++)foreach(int sign in new[]{-1,1})
                Box(stonework,new(x+sign*(w/2-.09f),(top+bottom)/2,row+.5f),new(.12f,top-bottom-.35f,.23f),_masonry,.025f);
        }
        void Tile(int x,int z,CellDef cell,bool bridge)
        {
            float y=cell.Tier*HeightScale;
            if(cell.Kind==CellKind.Ramp)
            {
                float from=cell.RampFromTier*HeightScale,to=cell.RampToTier*HeightScale;
                float angle=Mathf.Atan2(to-from,1)*cell.RampDX;
                var ramp=new Node3D {Position=new(x+.5f,(from+to)/2,z+.5f),Rotation=new(0,0,angle)};
                stonework.AddChild(ramp);
                float length=Mathf.Sqrt(1+(to-from)*(to-from));
                Box(ramp,new(0,-.095f,0),new(length,.19f,.99f),pale,.015f);
                Box(ramp,new(0,.009f,0),new(length,.018f,.72f),purple);
                for(int tread=0;tread<4;tread++)
                    Box(ramp,new((tread/4f-.5f)*length,.021f,0),new(.035f,.025f,.76f),purpleLight,.006f);
                return;
            }
            float thickness=bridge?CellDef.DeckThickness*HeightScale:.18f;
            Box(stonework,new(x+.5f,y-thickness/2,z+.5f),new(1,thickness,1),_masonry,.012f);
            Box(stonework,new(x+.5f,y-.025f,z+.5f),new(.98f,.05f,.98f),_stone,.012f);
            if(bridge && (x is 2 or 21 || z is 2 or 21))
            {
                Box(stonework,new(x+.5f,y+.004f,z+.5f),new(.79f,.008f,.79f),purple,.025f);
                Box(stonework,new(x+.5f,y+.012f,z+.14f),new(.59f,.012f,.024f),purpleLight);
            }
            // Entry arrows are inlays: they explain the flights without adding rails.
            if(!bridge && cell.Tier==2 && (x is 6 or 17) && (z is 7 or 14 or 21))
                Arrow(new(x+.5f,y+.018f,z+.5f),x==6?1:-1);
        }
        void Arrow(Vector3 at,int direction)
        {
            Box(stonework,at,new(.72f,.018f,.66f),purple,.015f);
            var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
            Vector3[] points=[new(direction*.23f,0,0),new(-direction*.19f,0,-.22f),new(-direction*.19f,0,.22f)];
            if(direction>0)Array.Reverse(points);
            foreach(var point in points){mesh.SetNormal(Vector3.Up);mesh.AddVertex(point);}
            stonework.AddChild(new MeshInstance3D {Position=at+new Vector3(0,.014f,0),Mesh=mesh.Commit(),MaterialOverride=brass});
        }
        void Frieze(Vector3 position,float angle)
        {
            var root=new Node3D {Position=position,Rotation=new(0,angle,0)};stonework.AddChild(root);
            Box(root,Vector3.Zero,new(.7f,.47f,.028f),_bedrock,.025f);
            foreach(int side in new[]{-1,1})
            {
                Box(root,new(side*.31f,0,.026f),new(.065f,.46f,.035f),pale,.01f);
                Box(root,new(0,side*.2f,.026f),new(.64f,.065f,.035f),pale,.01f);
            }
            Box(root,new(0,0,.032f),new(.3f,.22f,.034f),_masonry,.018f);
        }
    }
}
