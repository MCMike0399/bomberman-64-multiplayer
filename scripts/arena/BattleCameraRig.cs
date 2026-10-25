using Godot;
using System.Text.Json;
namespace BlastCircuit.Arena;

/// <summary>Fits playable terrain and standing characters into the space between the HUD and footer.</summary>
public sealed class BattleCameraRig(Camera3D camera)
{
    private readonly List<Vector3> _bounds=[];
    private Vector3 _center=new(10.5f,2,11.5f);
    private Vector2 _offset;
    private bool _training, _castle;
    public void SetStage(JsonElement stage)
    {
        _bounds.Clear();
        _castle=stage.GetProperty("id").GetString()=="top-rules";
        _training=stage.GetProperty("id").GetString()=="circuit-garden";
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
        var direction=_castle?new Vector3(0,.72f,.694f):new Vector3(0,.60f,.80f);
        // Centered frontal view keeps the broad stepped silhouette and visible vertical risers.
        bool Place(float distance)
        {
            camera.Position=_center+direction*distance;camera.LookAt(_center);
            camera.HOffset=0;camera.VOffset=0;
            for(int pass=0;pass<5;pass++)
            {
                Vector2 min=new(float.MaxValue,float.MaxValue),max=new(float.MinValue,float.MinValue);
                foreach(var point in _bounds){var screen=camera.UnprojectPosition(point)/viewport;min=min.Min(screen);max=max.Max(screen);}
                // Keep the full playable silhouette inside the HUD-safe band while using more
                // of the available screen for the arena instead of the surrounding water.
                var delta=(min+max)*.5f-new Vector2(.5f,_training?.445f:.5175f);
                if(pass==4)return min.X>=.025f&&max.X<=.975f&&min.Y>=.145f&&max.Y<=(_training?.745f:.89f);
                var origin=camera.UnprojectPosition(_center);
                float pixelsX=(camera.UnprojectPosition(_center+camera.GlobalBasis.X)-origin).Length();
                float pixelsY=(camera.UnprojectPosition(_center+camera.GlobalBasis.Y)-origin).Length();
                camera.HOffset+=delta.X*viewport.X/Mathf.Max(pixelsX,.01f);
                camera.VOffset-=delta.Y*viewport.Y/Mathf.Max(pixelsY,.01f);
            }
            return false;
        }
        float near=12,far=110;
        for(int i=0;i<22;i++){float mid=(near+far)*.5f;if(Place(mid))far=mid;else near=mid;}
        Place(far);
        _offset=new(camera.HOffset,camera.VOffset);
    }
    public void Shake(float time,float intensity)
    {
        camera.HOffset=_offset.X+Mathf.Sin(time*75)*intensity;
        camera.VOffset=_offset.Y+Mathf.Cos(time*83)*intensity*.5f;
    }
}
