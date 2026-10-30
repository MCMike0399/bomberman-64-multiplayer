using Godot;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildGreedyTrap()
    {
        for(int style=0;style<6;style++)_greedyMaterials.Add(_art.GreedyTrap(style));
        var court=_greedyMaterials[0];var brick=_greedyMaterials[1];var teal=_greedyMaterials[2];
        var ashlar=_greedyMaterials[3];var paving=_greedyMaterials[4];var brass=_greedyMaterials[5];
        var ink=_art.Color(new("383c39"));var coping=_art.Color(new("dfa94f"));
        Box(this,new(Width/2f,-.38f,Depth/2f),new(Width,.7f,Depth),brick).Name="GreedyFoundation";
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var cell=Layout.At(x,z);float y=cell.Tier*HeightScale;
            if(cell.Kind==CellKind.Pillar)
            {
                float crown=y+cell.ObstacleHeight!.Value*HeightScale;
                var wall=Box(this,new(x+.5f,(crown-.11f)/2,z+.5f),new(1,crown-.11f,1),brick);wall.Name=$"GreedyWallBrick_{x}_{z}";
                var cap=Box(this,new(x+.5f,crown-.055f,z+.5f),new(1.015f,.11f,1.015f),ink,.025f);cap.Name=$"GreedyWallCap_{x}_{z}";
                if(z>0&&z<Depth-1)
                {
                    if(x==0){_greedyOuterLeft.Add(wall);_greedyOuterLeft.Add(cap);}
                    if(x==Width-1){_greedyOuterRight.Add(wall);_greedyOuterRight.Add(cap);}
                }
                continue;
            }
            if(cell.Kind==CellKind.Ramp)
            {
                float from=cell.RampFromTier*HeightScale,to=cell.RampToTier*HeightScale;
                for(int step=0;step<8;step++)
                {
                    float t=(step+.5f)/8-.5f,top=Mathf.Lerp(from,to,(step+1)/8f);
                    var center=new Vector3(x+.5f+cell.RampDX*t,top/2,z+.5f+cell.RampDZ*t);
                    Box(this,center,new(cell.RampDX==0?.998f:1f/8,top,cell.RampDZ==0?.998f:1f/8),ashlar).Name=$"GreedyStep_{x}_{z}_{step}";
                    Box(this,new(center.X,top+.008f,center.Z),new(cell.RampDX==0?.83f:1f/8,.024f,cell.RampDZ==0?.83f:1f/8),paving).Name=$"GreedyTread_{x}_{z}_{step}";
                }
                continue;
            }
            if(y>0)Box(this,new(x+.5f,y/2-.045f,z+.5f),new(.999f,y-.09f,.999f),ashlar);
            Box(this,new(x+.5f,y-.035f,z+.5f),new(.998f,.07f,.998f),cell.Tier==2?court:cell.Tier==1?paving:teal).Name=$"GreedyFloor_{x}_{z}";
            // Warm coping sits just above the court surface. Its small visual offset
            // leaves authoritative drop geometry unchanged.
            if(cell.Tier==2)foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var neighbor=Layout.At(x+dx,z+dz);
                if(neighbor.IsPassable&&neighbor.Tier<2&&neighbor.Kind!=CellKind.Ramp)
                    Box(this,new(x+.5f+dx*.445f,y-.005f,z+.5f+dz*.445f),new(dx==0?1:.11f,.05f,dz==0?1:.11f),coping,.012f);
            }
            // A thin brass sill sits just above the middle ring to avoid coplanar faces.
            if(cell.Tier==1)foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                if(Layout.At(x+dx,z+dz) is {Kind:CellKind.Floor,Tier:0})
                    Box(this,new(x+.5f+dx*.475f,y+.002f,z+.5f+dz*.475f),new(dx==0?.99f:.045f,.036f,dz==0?.99f:.045f),brass);
        }
        // Original mechanical details on the solid rear wall, above the court.
        foreach(float x in new[]{3.5f,15.5f})
        {
            Box(this,new(x,4.05f,1.012f),new(1.2f,1.3f,.055f),ink,.05f);
            var hub=new Node3D{Name=$"GreedyFlywheel_{x}",Position=new(x,4.05f,1.075f)};AddChild(hub);_greedyGears.Add(hub);
            var ring=new MeshInstance3D{Mesh=PrimitiveTemplates.Get("TorusMesh",("inner_radius",.32f),("outer_radius",.42f),("rings",24),("ring_segments",8)),MaterialOverride=brass,RotationDegrees=new(90,0,0)};hub.AddChild(ring);
            for(int tooth=0;tooth<8;tooth++)
            {
                float angle=tooth*Mathf.Tau/8;
                var bar=Box(hub,new(Mathf.Cos(angle)*.43f,Mathf.Sin(angle)*.43f,0),new(.16f,.16f,.11f),coping,.02f);bar.Rotation=new(0,0,angle);
            }
            Box(hub,Vector3.Zero,new(.18f,.18f,.13f),coping,.035f);
        }
        BuildGreedyExterior();
    }

    private Node3D CreateGreedyCover(int x,int z)
    {
        var root=new Node3D{Position=new(x+.5f,Surface(x+.5f,z+.5f),z+.5f)};
        Box(root,new(0,.40f,0),new(.87f,.80f,.87f),_greedyMaterials[3],.028f);
        Box(root,new(0,.79f,0),new(.9f,.04f,.9f),_art.Color(new("9ba99a")),.015f);
        return root;
    }

    /// <summary>Collision changes immediately with the snapshot. Only the rear
    /// faces settle to the accepted boundary in 120 ms, like actor snapshot
    /// interpolation. Flywheels and inset warning strips animate independently.</summary>
    public void SetGreedyCompression(int left,int right)
    {
        if(!_greedy)return;
        if(_greedyCrushers.Count==0)
        {
            foreach(string side in new[]{"Left","Right"})
            {
                var root=new Node3D{Name="GreedyCrusher"+side};AddChild(root);_greedyCrushers.Add(root);
                float height=(2+(Layout.At(0,9).ObstacleHeight??1.8f))*HeightScale;
                Box(root,new(0,(height-.14f)/2,Depth/2f),new(1,height-.14f,Depth-2),_greedyMaterials[1]);
                Box(root,new(0,height-.07f,Depth/2f),new(1.015f,.14f,Depth-2),_art.Color(new("383c39")),.02f);
                foreach(float face in new[]{-.505f,.505f})
                    Box(root,new(face,2.9f,Depth/2f),new(.025f,.12f,Depth-2),_greedyMaterials[5]);
            }
        }
        if(left==0)_greedyFrontLeft=_greedyFrontRight=0;
        if(left!=_greedyClosed||right!=_greedyClosedRight||!_greedyWallsInitialized)
        {
            // Fill the whole closed strip behind each moving face. A one-cell
            // slab leaves a false walkable-looking lane against the old wall.
            var leftPosition=new Vector3(Math.Max(.5f,left/2f),0,0);
            var rightPosition=new Vector3(Math.Min(Width-.5f,Width-right/2f),0,0);
            _greedyLeftTarget=leftPosition;_greedyRightTarget=rightPosition;
            if(!_greedyWallsInitialized||left==0)
            {_greedyCrushers[0].Position=leftPosition;_greedyCrushers[1].Position=rightPosition;_greedyWallAge=.12f;}
            else
            {
                _greedyLeftFrom=_greedyCrushers[0].Position;_greedyRightFrom=_greedyCrushers[1].Position;_greedyWallAge=0;
            }
            _greedyWallsInitialized=true;_greedyGearImpulse=1;
        }
        SizeGreedyCrushers();
        ShowGreedyCrushers(Math.Max(left,_greedyFrontLeft),Math.Max(right,_greedyFrontRight));
        _greedyClosed=left;_greedyClosedRight=right;
        _greedyMaterials[5].SetShaderParameter("compression",left>0?1:0);
    }

    /// <summary>Creeps both crusher faces toward the next closure between authoritative steps.</summary>
    public void AdvanceGreedyCompression(float left,float right)
    {
        if(!_greedy||!_greedyWallsInitialized)return;
        _greedyFrontLeft=left;_greedyFrontRight=right;
        _greedyLeftTarget=new(Math.Max(.5f,left/2),0,0);_greedyRightTarget=new(Math.Min(Width-.5f,Width-right/2),0,0);
        _greedyCrushers[0].Position=_greedyLeftTarget;_greedyCrushers[1].Position=_greedyRightTarget;_greedyWallAge=.12f;
        SizeGreedyCrushers();ShowGreedyCrushers(left,right);
    }

    private void ShowGreedyCrushers(float left,float right)
    {
        _greedyCrushers[0].Visible=left>1;_greedyCrushers[1].Visible=right>1;
        foreach(var wall in _greedyOuterLeft)wall.Visible=left<=1;
        foreach(var wall in _greedyOuterRight)wall.Visible=right<=1;
    }

    private void ProcessGreedyTrap(float delta)
    {
        if(_greedyWallsInitialized&&_greedyWallAge<.12f)
        {
            _greedyWallAge=Math.Min(.12f,_greedyWallAge+delta);float ease=1-Mathf.Pow(1-_greedyWallAge/.12f,3);
            _greedyCrushers[0].Position=_greedyLeftFrom.Lerp(_greedyLeftTarget,ease);
            _greedyCrushers[1].Position=_greedyRightFrom.Lerp(_greedyRightTarget,ease);
            SizeGreedyCrushers();
        }
        if(_graphicsRevision!=GraphicsQuality.Revision)
        {
            foreach(var material in _greedyMaterials)GraphicsQuality.ApplyShaderDetail(material);
            UpdateGreedyExteriorQuality();
            _graphicsRevision=GraphicsQuality.Revision;
        }
        foreach(var gear in _greedyGears)gear.RotateZ(delta*_greedyGearImpulse*2.4f);
        _greedyGearImpulse=Mathf.MoveToward(_greedyGearImpulse,0,delta*2.5f);
        if(_greedyWaterwheel is {} wheel&&wheel.IsVisibleInTree())wheel.RotateZ(delta*.24f);
    }

    private void SizeGreedyCrushers()
    {
        _greedyCrushers[0].Scale=new(_greedyCrushers[0].Position.X*2,1,1);
        _greedyCrushers[1].Scale=new((Width-_greedyCrushers[1].Position.X)*2,1,1);
    }
}
