using Godot;
using System.Text.Json;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Arena;

/// <summary>Fits playable terrain and standing characters into the space between the HUD and footer.</summary>
public sealed partial class BattleCameraRig(Camera3D camera)
{
    private readonly List<Vector3> _bounds=[];
    private Vector3 _center=new(10.5f,2,11.5f);
    private Vector2 _offset;
    private bool _training, _castle, _blizzard, _pyramid, _garden;
    /// <summary>View depth of the closest/farthest playable point; depth cues stay outside this range.</summary>
    public Vector2 PlayableDepth {get;private set;}=new(20,40);
    /// <summary>Radial camera distance for depth fog, which uses length(view position), not axial depth.</summary>
    public float PlayableDistance {get;private set;}=40;
    public void SetStage(JsonElement stage)
    {
        _bounds.Clear();
        _garden=stage.GetProperty("id").GetString()=="rock-garden";
        _blizzard=stage.GetProperty("id").GetString()=="blizzard-battle";
        _castle=stage.GetProperty("id").GetString() is "top-rules" or "greedy-trap";
        _training=stage.GetProperty("id").GetString()=="circuit-garden";
        _pyramid=stage.GetProperty("id").GetString()=="sunken-temple";
        int width=stage.GetProperty("width").GetInt32(),depth=stage.GetProperty("height").GetInt32();
        _center=new(width*.5f,2,depth*.5f);
        int i=0;
        foreach(var cell in stage.GetProperty("cells").EnumerateArray())
        {
            int x=i%width,z=i++/width;
            AddCell(cell,x,z);
        }
        Fit();
        void AddCell(JsonElement cell,int x,int z)
        {
            if(cell.TryGetProperty("below",out var lower)&&lower.ValueKind==JsonValueKind.Object)AddCell(lower,x,z);
            int kind=cell.GetProperty("k").GetInt32();
            if(kind is 2 or 3)return;
            float y=(kind==4?cell.GetProperty("rt"):cell.GetProperty("t")).GetSingle()*TempleStageView.HeightScale;
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})
            {
                _bounds.Add(new(x+dx,y,z+dz));
                // Match solid pillar crowns and keep standing-player headroom on walkable cells.
                _bounds.Add(new(x+dx,y+(kind==1?(cell.TryGetProperty("oh",out var crown)&&crown.ValueKind==JsonValueKind.Number?crown.GetSingle()*TempleStageView.HeightScale:1.21f):2.1f),z+dz));
            }
        }
    }
    public void Fit()
    {
        if(_bounds.Count==0)return;
        var viewport=camera.GetViewport().GetVisibleRect().Size;
        if(viewport.X<=0||viewport.Y<=0)return;
        // The tall Pyramid otherwise hits the vertical limit with sea filling its sides.
        // A 30-degree elevation lets the same HUD-safe fit bring its terraces closer.
        var direction=_castle||_blizzard?new Vector3(0,.72f,.694f):
            _pyramid?new Vector3(0,.5f,Mathf.Sqrt(.75f)):new Vector3(0,.60f,.80f);
        // Centered frontal view keeps the broad stepped silhouette and visible vertical risers.
        bool Place(float distance)
        {
            camera.Position=_center+direction*distance;
            if(_garden)camera.Basis=new(Vector3.Right,new(0,.8f,-.6f),new(0,.6f,.8f));
            else camera.LookAt(_center);
            camera.HOffset=0;camera.VOffset=0;
            for(int pass=0;pass<5;pass++)
            {
                Func<Vector3,Vector2> project=_garden?PortableProjection(viewport):camera.UnprojectPosition;
                Vector2 min=new(float.MaxValue,float.MaxValue),max=new(float.MinValue,float.MinValue);
                foreach(var point in _bounds){var screen=project(point)/viewport;min=min.Min(screen);max=max.Max(screen);}
                // Keep the full playable silhouette inside the HUD-safe band while using more
                // of the available screen for the arena instead of the surrounding water.
                var delta=(min+max)*.5f-new Vector2(.5f,_training?.445f:.5175f);
                if(pass==4)return min.X>=.025f&&max.X<=.975f&&min.Y>=.145f&&max.Y<=(_training?.745f:.89f);
                var origin=project(_center);
                float pixelsX=(project(_center+camera.GlobalBasis.X)-origin).Length();
                float pixelsY=(project(_center+camera.GlobalBasis.Y)-origin).Length();
                camera.HOffset+=delta.X*viewport.X/Mathf.Max(pixelsX,.01f);
                camera.VOffset-=delta.Y*viewport.Y/Mathf.Max(pixelsY,.01f);
            }
            return false;
        }
        float near=12,far=110;
        for(int i=0;i<22;i++){float mid=(near+far)*.5f;if(Place(mid))far=mid;else near=mid;}
        Place(far);
        _offset=new(camera.HOffset,camera.VOffset);
        MeasureDepth();
    }
    /// <summary>Re-measure playable view depth for the camera's current transform.</summary>
    public void MeasureDepth()
    {
        if(_bounds.Count==0)return;
        if(_garden){MeasurePortableDepth();return;}
        float nearest=float.MaxValue,farthest=0,distance=0;
        var view=camera.GetCameraTransform().AffineInverse();
        foreach(var point in _bounds)
        {
            var position=view*point;float depth=-position.Z;
            nearest=Mathf.Min(nearest,depth);farthest=Mathf.Max(farthest,depth);
            distance=Mathf.Max(distance,position.Length());
        }
        PlayableDepth=new(Mathf.Max(nearest,.1f),farthest);
        PlayableDistance=distance;
    }
    public void Shake(float time,float intensity)
    {
        camera.HOffset=_offset.X+Mathf.Sin(time*75)*intensity;
        camera.VOffset=_offset.Y+Mathf.Cos(time*83)*intensity*.5f;
    }
}
