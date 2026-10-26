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
            // Moving bombs are uncertain. Cover both their visible position and short velocity projection.
            if(b.State is BombState.Rolling or BombState.Flying)
            {
                float horizon=Math.Min(time,.6f);float vx=b.State==BombState.Flying?b.FlyVX:b.VX,vz=b.State==BombState.Flying?b.FlyVZ:b.VZ;
                for(float t=.2f;t<=horizon+.01f;t+=.2f)
                {
                    float x=b.X+vx*t,z=b.Z+vz*t;
                    if(_graph.Cell(x,z)<0)break;
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
    public void AddHypothetical(SoftBlockGrid soft,BlastBomb bomb,float reactionMargin)
    {AddSphere(soft,bomb.X,Height(bomb),bomb.Z,bomb.BlastRadius+.45f,bomb.IsRed,Math.Max(0,bomb.FuseTimer-reactionMargin),bomb.FuseTimer+.55f);}
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
