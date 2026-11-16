using Bomber.Client.Sessions;
using Bomber.Game.Matches;

namespace BlastCircuit.Testing;

/// <summary>QA-only exclusive input ownership. Cached game-frame samples cannot
/// cross acquisition, disposal, session reset or connection-generation boundaries.</summary>
internal sealed class FixtureInputPublication
{
    internal readonly record struct Sample(long Epoch,ISession Session,int Seat,int Generation);
    private readonly object _sync=new();
    private long _epoch;
    private Lease? _owner;

    // Sampling and visual work stay outside the publication critical section.
    internal Sample Capture(ISession session,int seat)=>new(Volatile.Read(ref _epoch),session,seat,Generation(session));
    internal bool PublishFrame(Sample sample,PlayerInput input)
    {
        lock(_sync)
        {
            if(sample.Epoch!=_epoch||sample.Generation!=Generation(sample.Session)||
                _owner is {} owner&&ReferenceEquals(owner.Session,sample.Session)&&owner.Seat==sample.Seat)return false;
            sample.Session.SendInput(sample.Seat,input);return true;
        }
    }
    internal Lease Own(ISession session,int seat)
    {
        lock(_sync)
        {
            if(_owner!=null)throw new InvalidOperationException("A fixture already owns input publication");
            var lease=new Lease(this,session,seat,Generation(session),++_epoch);_owner=lease;return lease;
        }
    }
    internal void Invalidate()
    {lock(_sync){_owner=null;_epoch++;}}
    private static int Generation(ISession session)=>session is NetworkSession network?network.ConnectionGeneration:0;

    internal sealed class Lease(FixtureInputPublication publication,ISession session,int seat,int generation,long epoch):IDisposable
    {
        internal ISession Session=>session;
        internal int Seat=>seat;
        internal bool Publish(PlayerInput input)
        {
            lock(publication._sync)
            {
                if(!ReferenceEquals(publication._owner,this)||publication._epoch!=epoch||Generation(session)!=generation)return false;
                session.SendInput(seat,input);return true;
            }
        }
        public void Dispose()
        {
            lock(publication._sync)
                if(ReferenceEquals(publication._owner,this)){publication._owner=null;publication._epoch++;}
        }
    }
}
