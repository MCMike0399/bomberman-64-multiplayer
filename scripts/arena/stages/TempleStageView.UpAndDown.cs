using Godot;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

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
        var purple=_art.Color(new("652b7e"),.58f);
        var purpleLight=_art.Color(new("cda6dd"),.6f);
        var violetDark=_art.Color(new("3b3556"));
        var brass=_art.Color(new("e6cf67"),.5f);
        var stonework=new Node3D {Name="UpAndDownStonework"};AddChild(stonework);
        // Three long piers, each rooted below the water. The crown is a separate
        // thick cap above the central pier; bridges never receive these shafts.
        Pier(3,6,7,23,2); Pier(17,20,7,23,2); Pier(10,13,12,23,0); Pier(10,13,7,10,4);
        var crown=Box(stonework,new(12,4*HeightScale-.51f,9),new(4,.66f,4),_masonry,.045f);
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
        foreach(var (x0,x1,z0,z1,tier) in new[]{(3,6,7,23,2),(17,20,7,23,2),(10,13,12,23,0),(10,13,7,10,4)})
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
            // A recessed core keeps the existing solid footprint; paired rounded
            // shafts recover the original silhouette without adding false routes.
            Box(stonework,new(x,(top+bottom)/2,z),new(w-1.15f,top-bottom,d-1.4f),_bedrock,.08f);
            Box(stonework,new(x,top-.28f,z),new(w,.4f,d),_masonry,.035f);
            foreach(float sx in new[]{x0+.7f,x1+.3f})foreach(float sz in new[]{z0+.7f,z1+.3f})
            {
                float radius=.63f;
                Cylinder(stonework,new(sx,(top+bottom)/2,sz),radius,top-bottom,_masonry);
                for(int flute=0;flute<12;flute++)
                {
                    float angle=flute*Mathf.Tau/12;
                    Cylinder(stonework,new(sx+Mathf.Cos(angle)*radius*.9f,(top+bottom)/2,sz+Mathf.Sin(angle)*radius*.9f),.055f,top-bottom-.25f,_stone);
                }
                foreach(float band in new[]{top-.48f,top-1.65f})Cylinder(stonework,new(sx,band,sz),radius+.035f,.16f,_stone);
            }
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
            // The paving owns the visible top. Coplanar slab/cap faces used to
            // fight in both the battle camera and the scaled menu miniature.
            Box(stonework,new(x+.5f,y-(thickness+.06f)/2,z+.5f),new(1,thickness-.06f,1),_masonry,.012f);
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

    /// <summary>Trim this display instance without modifying shared gameplay meshes.</summary>
    internal void FitUpAndDownMiniature(float radius,float bottom)
    {
        if(Layout.Id!=BlastStages.UpAndDown)return;
        // An opaque circular sea belongs to the round stand. Screen refraction
        // here would sample the unrelated menu theatre outside the miniature.
        _water.Mesh=new CylinderMesh {TopRadius=radius,BottomRadius=radius,Height=.02f,RadialSegments=96};
        _water.MaterialOverride=_art.Color(new("246c80"),.55f);
        Trim(this);
        void Trim(Node parent)
        {
            foreach(var child in parent.GetChildren())
            {
                if(child is MeshInstance3D {Mesh:not null} mesh && mesh!=_water && mesh.Visible)
                {
                    var bounds=mesh.Transform*mesh.Mesh.GetAabb();
                    if(bounds.End.Y<=bottom)mesh.Visible=false;
                    else if(bounds.Position.Y<bottom)
                    {
                        float ratio=(bounds.End.Y-bottom)/bounds.Size.Y;
                        var scale=mesh.Scale;scale.Y*=ratio;mesh.Scale=scale;
                        var position=mesh.Position;position.Y+=(bounds.End.Y-position.Y)*(1-ratio);mesh.Position=position;
                    }
                }
                Trim(child);
            }
        }
    }

    private Node3D CreateUpAndDownCover(Node3D root,int cell)
    {
        root.Name="UpAndDownBlock"+cell;
        // Reference-inspired gray stone cubes with a worn rim and recessed
        // square faces, rather than the temple's gold caps/diamond ornament.
        float height=BombThrowRules.CoverHeight*HeightScale;
        var stone=_art.Color(new("a5aaa3"),.9f);
        var edge=_art.Color(new("d1d4bf"),.88f);
        var recess=_art.Color(new("707d77"),.95f);
        Box(root,new(0,(height-.07f)/2,0),new(.8f,height-.07f,.8f),stone,.045f);
        Box(root,new(0,height-.035f,0),new(.84f,.07f,.84f),edge,.025f);
        foreach(int direction in new[]{-1,1})
        {
            Box(root,new(0,height*.49f,direction*.404f),new(.54f,height*.56f,.018f),recess,.018f);
            Box(root,new(direction*.404f,height*.49f,0),new(.018f,height*.56f,.54f),recess,.018f);
            Box(root,new(0,height*.49f,direction*.416f),new(.36f,height*.38f,.012f),stone,.012f);
            Box(root,new(direction*.416f,height*.49f,0),new(.012f,height*.38f,.36f),stone,.012f);
        }
        return root;
    }
}
