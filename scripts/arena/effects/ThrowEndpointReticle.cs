using Godot;
using Bomber.Game.Simulation;
using BlastCircuit.UI;
namespace BlastCircuit.Arena.Effects;

/// <summary>Screen-space endpoint; terrain may hide the arc but never the throw outcome.</summary>
public partial class ThrowEndpointReticle : Node2D
{
    private Color _color;
    private ThrowProjectionEnd _end;
    private Vector2 _direction;
    public bool EdgeClamped {get;private set;}
    public void Update(Camera3D camera,Vector3 endpoint,ThrowProjectionEnd end,Color color,bool active)
    {
        Visible=active;if(!active)return;
        _end=end;_color=end==ThrowProjectionEnd.Landing?color:ArcadeTheme.Gold;
        var size=camera.GetViewport().GetVisibleRect().Size;
        var projected=camera.UnprojectPosition(endpoint);
        bool behind=camera.IsPositionBehind(endpoint);
        var center=size/2;
        _direction=(projected-center)*(behind?-1:1);
        if(_direction.LengthSquared()<.01f)_direction=Vector2.Up;
        var safe=new Rect2(new Vector2(110,120),size-new Vector2(220,245));
        EdgeClamped=behind||!safe.HasPoint(projected);
        if(EdgeClamped)
        {
            // Intersect the ray with the safe frame so the arrow keeps its direction.
            var half=safe.Size/2;var safeCenter=safe.GetCenter();
            float scale=Math.Min(half.X/Math.Max(.001f,Math.Abs(_direction.X)),half.Y/Math.Max(.001f,Math.Abs(_direction.Y)));
            Position=safeCenter+_direction*scale;
        }
        else Position=projected;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if(EdgeClamped)
        {
            var d=_direction.Normalized();var side=new Vector2(-d.Y,d.X);
            Vector2[] arrow=[d*18,-d*10+side*10,-d*5,-d*10-side*10,d*18];
            DrawPolyline(arrow,ArcadeTheme.Ink,9,true);DrawPolyline(arrow,_color,3,true);
        }
        else if(_end==ThrowProjectionEnd.Landing)
        {
            DrawArc(Vector2.Zero,15,0,Mathf.Tau,48,ArcadeTheme.Ink,8,true);
            DrawArc(Vector2.Zero,15,0,Mathf.Tau,48,_color,3,true);
            Stroke(new(-22,0),new(-10,0));Stroke(new(10,0),new(22,0));
            Stroke(new(0,-22),new(0,-10));Stroke(new(0,10),new(0,22));
        }
        else
        {
            Stroke(new(-13,-13),new(13,13));Stroke(new(-13,13),new(13,-13));
        }
    }
    private void Stroke(Vector2 a,Vector2 b)
    {DrawLine(a,b,ArcadeTheme.Ink,9,true);DrawLine(a,b,_color,3,true);}
}
