using Godot;
using System.Text.Json;
namespace BlastCircuit.Rendering;

/// <summary>Fits playable terrain and standing characters into the space between the HUD and footer.</summary>
public sealed class BattleCameraRig(Camera3D camera)
{
    private readonly List<Vector3> _bounds=[];
    private Vector3 _center=new(10.5f,2,11.5f);
    private Vector2 _offset;
    public void SetStage(JsonElement stage)
    {
        _bounds.Clear();
        int width=stage.GetProperty("width").GetInt32(),depth=stage.GetProperty("height").GetInt32();
        _center=new(width*.5f,2,depth*.5f);
        int i=0;
        foreach(var cell in stage.GetProperty("cells").EnumerateArray())
        {
            int x=i%width,z=i++/width,kind=cell.GetProperty("k").GetInt32();
            if(kind is 2 or 3)continue;
            float y=(kind==4?cell.GetProperty("rt"):cell.GetProperty("t")).GetSingle()*TempleStageView.HeightScale;
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})
            {
                _bounds.Add(new(x+dx,y,z+dz));
                _bounds.Add(new(x+dx,y+2.1f,z+dz));
            }
        }
        Fit();
    }
    public void Fit()
    {
        if(_bounds.Count==0)return;
        var viewport=camera.GetViewport().GetVisibleRect().Size;
        if(viewport.X<=0||viewport.Y<=0)return;
        var direction=new Vector3(.075f,.735f,.674f).Normalized();
        // Slight yaw preserves the familiar temple orientation; the higher angle reveals routes behind cover.
        bool Place(float distance)
        {
            camera.Position=_center+direction*distance;camera.LookAt(_center);
            camera.HOffset=0;camera.VOffset=0;
            for(int pass=0;pass<5;pass++)
            {
                Vector2 min=new(float.MaxValue,float.MaxValue),max=new(float.MinValue,float.MinValue);
                foreach(var point in _bounds){var screen=camera.UnprojectPosition(point)/viewport;min=min.Min(screen);max=max.Max(screen);}
                var delta=(min+max)*.5f-new Vector2(.5f,.515f);
                if(pass==4)return min.X>=.035f&&max.X<=.965f&&min.Y>=.155f&&max.Y<=.875f;
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
