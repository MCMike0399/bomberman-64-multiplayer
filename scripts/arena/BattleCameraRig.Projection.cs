using BlastCircuit.Rendering;
using Godot;
namespace BlastCircuit.Arena;

public sealed partial class BattleCameraRig
{
    // Rock Garden's measured fit crossed one binary-search step through native
    // projection arithmetic. Evaluate this fit in a single managed operation order.
    private Func<Vector3,(double X,double Y,double Z)> PortableView()
    {
        var basis=camera.GlobalBasis;var origin=camera.GlobalPosition;
        double ex=origin.X+(double)basis.X.X*camera.HOffset+(double)basis.Y.X*camera.VOffset;
        double ey=origin.Y+(double)basis.X.Y*camera.HOffset+(double)basis.Y.Y*camera.VOffset;
        double ez=origin.Z+(double)basis.X.Z*camera.HOffset+(double)basis.Y.Z*camera.VOffset;
        return point=>
        {
            double x=point.X-ex,y=point.Y-ey,z=point.Z-ez;
            return (x*basis.X.X+y*basis.X.Y+z*basis.X.Z,
                x*basis.Y.X+y*basis.Y.Y+z*basis.Y.Z,
                x*basis.Z.X+y*basis.Z.Y+z*basis.Z.Z);
        };
    }
    private Func<Vector3,Vector2> PortableProjection(Vector2 viewport)
    {
        if(camera.Projection!=Camera3D.ProjectionType.Perspective)throw new InvalidOperationException("Rock Garden fit requires its perspective camera");
        var (sin,cos)=PresentationMath.SinCos(camera.Fov*(Mathf.Pi/180)*.5f);
        double tangent=(double)sin/cos,aspect=(double)viewport.X/viewport.Y;
        double xScale=1/tangent,yScale=1/tangent;
        if(camera.KeepAspect==Camera3D.KeepAspectEnum.Height)xScale/=aspect;else yScale*=aspect;
        var view=PortableView();
        return point=>
        {
            var p=view(point);
            return new((float)((p.X/-p.Z*xScale+1)*.5*viewport.X),
                (float)((1-p.Y/-p.Z*yScale)*.5*viewport.Y));
        };
    }
    private void MeasurePortableDepth()
    {
        var view=PortableView();double nearest=double.MaxValue,farthest=0,distanceSquared=0;
        foreach(var point in _bounds)
        {
            var p=view(point);nearest=Math.Min(nearest,-p.Z);farthest=Math.Max(farthest,-p.Z);
            distanceSquared=Math.Max(distanceSquared,p.X*p.X+p.Y*p.Y+p.Z*p.Z);
        }
        PlayableDepth=new(Mathf.Max((float)nearest,.1f),(float)farthest);
        PlayableDistance=(float)Math.Sqrt(distanceSquared);
    }
}
