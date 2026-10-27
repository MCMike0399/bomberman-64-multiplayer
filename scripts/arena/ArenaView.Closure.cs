using Godot;
using System.Text.Json;
using Bomber.Game.Simulation.Stages;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Arena;

/// <summary>Hurry closing walls. Authority seals one whole column per interval; between
/// closures the leading face creeps across the next column so it arrives exactly when that
/// column becomes lethal, instead of the wall jumping a full cell at once.</summary>
public partial class ArenaView
{
    // Negative keys share _walls so every existing reset path also frees the moving faces.
    private const int LeftFront=-1,RightFront=-2,LeftSlab=-3,RightSlab=-4;
    private int _closedLeftView,_closedRightView;
    private float _closureFront;
    private bool _closureAdvancing;

    private void UpdateClosure(JsonElement data,int left,int right)
    {
        if(_terrain==null)return;
        _terrain.SetGreedyCompression(left,right);
        if(left!=_closedLeftView||right!=_closedRightView)_closureFront=0;
        _closedLeftView=left;_closedRightView=right;
        string mode=data.GetProperty("hurryMode").GetString()??"none";
        _closureAdvancing=(int)F(data,"phase")==2&&mode is "columns" or "avalanche"&&left+right<_terrain.Width-1;
        if(!_closureAdvancing)_closureFront=0;
        if(_stage==BlastStages.GreedyTrap)return;
        for(int x=0;x<_terrain.Width;x++)
        {
            bool closed=x<left||x>=_terrain.Width-right;
            if(closed&&!_walls.ContainsKey(x)&&!Translucent)
            {
                // The arriving face becomes the settled column, so nothing pops or swells.
                int key=x<_terrain.Width/2?LeftFront:RightFront;
                if(_walls.Remove(key,out var front)&&(int)front.GetMeta("column")==x){front.Position=Vector3.Zero;_walls[x]=front;}
                else{front?.QueueFree();_walls[x]=AddClosureColumn(x,swell:true);}
            }
            if(!closed&&_walls.Remove(x,out var wall))wall.QueueFree();
        }
        PoseClosureFronts();
    }

    /// <summary>Called every frame; local pacing is resynchronized by each accepted closure.</summary>
    private void AdvanceClosure(float dt)
    {
        if(_terrain==null||!_closureAdvancing||!_battleActive)return;
        _closureFront=Mathf.Min(1,_closureFront+dt/BlastStages.HurryColumnSeconds(_stage));
        if(_stage==BlastStages.GreedyTrap)_terrain.AdvanceGreedyCompression(_closedLeftView+_closureFront,_closedRightView+_closureFront);
        else PoseClosureFronts();
    }

    private bool Translucent=>_stage!=BlastStages.TopRules&&_stage!=BlastStages.BlizzardBattle;

    private void PoseClosureFronts()
    {
        if(_terrain==null)return;
        int width=_terrain.Width;
        float front=_closureAdvancing?_closureFront:0;
        if(Translucent)
        {
            // One continuous slab per side; a stack of translucent columns would band.
            Slab(LeftSlab,0,_closedLeftView+front);
            Slab(RightSlab,width,_closedRightView+front);
            return;
        }
        Front(LeftFront,_closedLeftView,-1);
        Front(RightFront,width-1-_closedRightView,1);
        void Front(int key,int column,int outward)
        {
            if(!_closureAdvancing||_closureFront<=0){if(_walls.Remove(key,out var stale))stale.QueueFree();return;}
            if(!_walls.TryGetValue(key,out var node)||(int)node.GetMeta("column")!=column)
            {
                if(node!=null)node.QueueFree();
                _walls[key]=node=AddClosureColumn(column,swell:false);
            }
            // Slide out from behind the previous column (or the arena wall).
            node.Position=new(outward*(1-_closureFront),0,0);
        }
        void Slab(int key,float origin,float extent)
        {
            if(extent<.001f){if(_walls.Remove(key,out var stale))stale.QueueFree();return;}
            if(!_walls.TryGetValue(key,out var node))
            {
                node=new Node3D{Name=key==LeftSlab?"ClosingWallLeft":"ClosingWallRight",Position=new(origin,0,0)};
                Box(node,new(key==LeftSlab?.5f:-.5f,2.1f,_terrain.Depth/2f),new(1,5.8f,_terrain.Depth),_art.Color(new Color(.92f,.18f,.17f,.4f),.7f,true));
                _effects.AddChild(node);_walls[key]=node;
            }
            node.Scale=new(extent,1,1);
        }
    }

    private Node3D AddClosureColumn(int x,bool swell)
    {
        var terrain=_terrain!;
        var n=_stage==BlastStages.BlizzardBattle?terrain.CreateAvalanche(x,swell):new Node3D();
        if(_stage==BlastStages.TopRules)
        {
            var stone=terrain.CastleWallMaterial;
            float wallHeight=(terrain.Layout.At(0,terrain.Depth/2).ObstacleHeight??3.9f)*TempleStageView.HeightScale;
            Box(n,new(x+.5f,wallHeight/2,terrain.Depth/2f),new(1,wallHeight,terrain.Depth-2),stone);
            Box(n,new(x+.5f,wallHeight+.05f,terrain.Depth/2f),new(1.03f,.16f,terrain.Depth-2),_art.Color(new Color("bdc4c5")),.025f);
        }
        n.SetMeta("column",x);
        _effects.AddChild(n);return n;
    }
}
