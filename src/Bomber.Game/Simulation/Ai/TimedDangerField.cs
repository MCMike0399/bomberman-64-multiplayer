using System.Numerics;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation.Ai;

/// <summary>Conservative time intervals from visible fuses, chain reactions and existing blast zones.
/// Multiple intervals in one cell are merged; this can reject a safe timing gap but cannot invent one.</summary>
internal sealed class TimedDangerField
{
    private readonly NavigationGraph _graph;
    private readonly StageLayout _stage;
    public float[] Begins {get;}
    public float[] Ends {get;}
    public string?[] BombOwners {get;}
    private float[] _times=new float[64];
    /// <summary>Visible weather forecast: wind displacement speed after the given number of seconds.</summary>
    public Func<float,Vector2>? Wind {get;set;}
    public TimedDangerField(NavigationGraph graph,StageLayout stage)
    { _graph=graph;_stage=stage;Begins=new float[graph.Count];Ends=new float[graph.Count];BombOwners=new string?[graph.Count];Clear(); }
    private void Clear(){Array.Fill(Begins,float.PositiveInfinity);Array.Fill(Ends,float.NegativeInfinity);Array.Clear(BombOwners);}
    public bool Safe(int cell,float enter,float leave) => leave<Begins[cell] || enter>Ends[cell];
    public void Build(SoftBlockGrid soft,IReadOnlyList<BlastBomb> bombs,IReadOnlyList<BlastHazard> hazards,IReadOnlyList<BlastMeteor>? meteors=null,BlastBomb? detonate=null)
    {
        Clear();
        if(_times.Length<bombs.Count)Array.Resize(ref _times,Math.Max(bombs.Count,_times.Length*2));
        for(int i=0;i<bombs.Count;i++)
        {
            var b=bombs[i];_times[i]=b.Exploded||b.State==BombState.Carried?float.PositiveInfinity:b.IsRemote?.65f:Math.Max(0,b.FuseTimer);
            if(ReferenceEquals(b,detonate)&&!b.Exploded&&b.State!=BombState.Carried)_times[i]=0;
            int cell=_graph.Cell(b.X,b.Z,Height(b));
            if(!b.Exploded&&b.State==BombState.Resting&&cell>=0)BombOwners[cell]=b.OwnerId??"unknown";
        }
        // Published impacts can ignite ordinary or remote bombs before their own fuse.
        if(meteors is not null)foreach(var meteor in meteors)
            for(int i=0;i<bombs.Count;i++)
            {
                var bomb=bombs[i];if(bomb.Exploded||bomb.State==BombState.Carried)continue;
                if(BlastGeometry.Contains(_stage,soft,meteor.X,0,meteor.Z,1.8f,bomb.X,Height(bomb),bomb.Z,true))
                    _times[i]=Math.Min(_times[i],Math.Max(0,meteor.Remaining-.15f));
            }
        // A visible short fuse can trigger a longer fuse; propagate until the earliest chain time stabilizes.
        // Soft cover may disappear before another detonation: future threats conservatively retain stone cover only.
        for(int pass=0;pass<bombs.Count;pass++)
        {
            bool changed=false;
            for(int i=0;i<bombs.Count;i++)if(float.IsFinite(_times[i]))
                for(int j=0;j<bombs.Count;j++)if(_times[j]>_times[i]&&bombs[j].State!=BombState.Carried&&!bombs[j].Exploded)
                {
                    var a=bombs[i];var b=bombs[j];
                    if(BlastGeometry.Contains(_stage,soft,a.X,Height(a),a.Z,a.BlastRadius+.5f,b.X,Height(b),b.Z,true))
                    {_times[j]=_times[i];changed=true;}
                }
            if(!changed)break;
        }
        for(int i=0;i<bombs.Count;i++)if(float.IsFinite(_times[i]))
        {
            var b=bombs[i];float time=_times[i];
            // A proposed remote command destroys cover before recursive chains
            // and lingering hazards. Its time-zero forecast cannot rely on that
            // cover as a shield; permanent terrain still blocks every sphere.
            bool ignoreSoftCover=b.IsRed||detonate is not null;
            AddSphere(soft,b.X,Height(b),b.Z,b.BlastRadius+.45f,ignoreSoftCover,time,b.IsRemote?4:time+.55f);
            // Gusts push resting bombs across the snow. Cover the forecast drift
            // path until detonation; players or bombs can stop it anywhere along it.
            if(Wind is not null&&b.State is BombState.Resting or BombState.Rolling)AddDrift(soft,b,time);
            // Moving bombs are uncertain. Cover both their visible position and short velocity projection.
            if(b.State is BombState.Rolling or BombState.Flying)
            {
                // A kicked bomb keeps rolling until something stops it; fleeing along
                // its line gets the bot knocked down beside its own blast.
                bool rolling=b.State==BombState.Rolling;
                if(rolling)AddRollImpact(soft,b);
                float horizon=Math.Min(time,rolling&&!b.IsBouncy?2f:.6f);float vx=b.State==BombState.Flying?b.FlyVX:b.VX,vz=b.State==BombState.Flying?b.FlyVZ:b.VZ;
                for(float t=.2f;t<=horizon+.01f;t+=.2f)
                {
                    float x=b.X+vx*t,z=b.Z+vz*t;
                    int along=_graph.Cell(x,z,Height(b));
                    if(along<0)break;
                    if(rolling&&!b.IsBouncy&&(!_graph.Walkable[along]||soft.IsAlive((int)MathF.Floor(x),(int)MathF.Floor(z))))break;

                    // Ricochets can reverse direction at stone; cover the reflected side as well.
                    if(b.IsBouncy)AddSphere(soft,b.X-vx*t,b.State==BombState.Flying?b.FlyY:_stage.SurfaceAt(b.X-vx*t,b.Z-vz*t,Height(b)),b.Z-vz*t,b.BlastRadius+.45f,b.IsRed,Math.Max(0,time-.15f),time+.65f);
                    if(b.State==BombState.Flying)AddSphere(soft,x,b.FlyY,z,b.BlastRadius+.45f,b.IsRed,Math.Max(0,time-.15f),time+.65f);
                    AddSphere(soft,x,_stage.SurfaceAt(x,z,Height(b)),z,b.BlastRadius+.45f,b.IsRed,Math.Max(0,time-.15f),time+.65f);
                }
            }
        }
        if(meteors is not null)foreach(var m in meteors)AddSphere(soft,m.X,0,m.Z,1.75f,false,Math.Max(0,m.Remaining-.15f),m.Remaining+.55f);
        foreach(var h in hazards)AddSphere(soft,h.X,h.Y,h.Z,h.Radius+.45f,h.IsRed,0,h.LifeTimer+.1f);
    }
    /// <summary>Impact knocks a body down for over a second. A kick covers more than two
    /// cells in a fifth of a second, so sample its path at sub-cell spacing from now.</summary>
    private void AddRollImpact(SoftBlockGrid soft,BlastBomb b)
    {
        float speed=MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ);
        if(speed<.01f)return;
        float y=Height(b),step=.4f/speed;
        for(float t=0;t<=1.2f;t+=step)
        {
            float x=b.X+b.VX*t,z=b.Z+b.VZ*t;
            int cell=_graph.Cell(x,z,y);
            if(cell<0||!b.IsBouncy&&(!_graph.Walkable[cell]||soft.IsAlive((int)MathF.Floor(x),(int)MathF.Floor(z))))break;
            AddSphere(soft,x,_stage.SurfaceAt(x,z,y),z,.95f,true,Math.Max(0,t-.3f),t+.2f);
        }
    }
    /// <summary>Immediate impact threat along the lane a kicked bomb would roll.</summary>
    public void AddKickLane(SoftBlockGrid soft,BlastBomb bomb,float dx,float dz)
    {
        float y=Height(bomb);
        for(float along=.5f;along<=KickLaneCells;along+=.5f)
        {
            float x=bomb.X+dx*along,z=bomb.Z+dz*along;
            int cell=_graph.Cell(x,z,y);
            if(cell<0||!_graph.Walkable[cell]||soft.IsAlive((int)MathF.Floor(x),(int)MathF.Floor(z)))break;
            AddSphere(soft,x,y,z,.95f,true,0,KickWindow);
        }
    }
    private const float KickLaneCells=7,KickWindow=.8f;
    public void AddHypothetical(SoftBlockGrid soft,BlastBomb bomb,float reactionMargin)
    {AddSphere(soft,bomb.X,Height(bomb),bomb.Z,bomb.BlastRadius+.45f,bomb.IsRed,Math.Max(0,bomb.FuseTimer-reactionMargin),bomb.FuseTimer+.55f);}
    private void AddDrift(SoftBlockGrid soft,BlastBomb b,float time)
    {
        float x=b.X,z=b.Z,y=Height(b),horizon=Math.Min(b.IsRemote?2:time,4),covered=0;
        const float step=.1f;
        for(float t=0;t<horizon;t+=step)
        {
            var wind=Wind!(t);
            if(wind==Vector2.Zero)continue;
            float nx=x+wind.X*step,nz=z+wind.Y*step;
            int cell=_graph.Cell(nx,nz,y);
            if(cell<0||!_graph.Walkable[cell]||Math.Abs(_graph.Height[cell]-y)>.3f||soft.IsAlive((int)MathF.Floor(nx),(int)MathF.Floor(nz)))break;
            covered+=MathF.Abs(nx-x)+MathF.Abs(nz-z);x=nx;z=nz;
            if(covered<.3f)continue;
            covered=0;AddSphere(soft,x,y,z,b.BlastRadius+.45f,b.IsRed,Math.Max(0,time-.15f),b.IsRemote?4:time+.55f);
        }
        AddSphere(soft,x,y,z,b.BlastRadius+.45f,b.IsRed,Math.Max(0,time-.15f),b.IsRemote?4:time+.55f);
    }
    private float Height(BlastBomb b)=>b.State==BombState.Flying?b.FlyY:b.State==BombState.Resting?b.LandingY??_stage.SurfaceAt(b.X,b.Z,b.Tier):_stage.SurfaceAt(b.X,b.Z,b.Tier);
    private void AddSphere(SoftBlockGrid soft,float x,float y,float z,float radius,bool red,float begin,float end)
    {
        int minX=Math.Max(0,(int)MathF.Floor(x-radius)),maxX=Math.Min(_stage.Width-1,(int)MathF.Ceiling(x+radius));
        int minZ=Math.Max(0,(int)MathF.Floor(z-radius)),maxZ=Math.Min(_stage.Height-1,(int)MathF.Ceiling(z+radius));
        for(int cz=minZ;cz<=maxZ;cz++)for(int cx=minX;cx<=maxX;cx++)
        {
            for(int cell=cz*_graph.Width+cx;cell<_graph.Count;cell+=_stage.Width*_stage.Height)
            {
                if(_graph.Walkable[cell]&&BlastGeometry.HitsPlayer(_stage,soft,x,y,z,radius,cx+.5f,_graph.Height[cell],cz+.5f,red||begin>0))
                {Begins[cell]=Math.Min(Begins[cell],begin);Ends[cell]=Math.Max(Ends[cell],end);}
            }
        }
    }
}
