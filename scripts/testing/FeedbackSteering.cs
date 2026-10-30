using Bomber.Game.Matches;

namespace BlastCircuit.Testing;

// Synthetic journey controller only; real player controls and authority are unchanged.
public static class FeedbackSteering
{
    public static PlayerInput Toward(float x,float z,bool delayedPeerFeedback)
    {
        // A delayed observation plus up to five authority ticks needs a
        // damped approach at narrow landings, rather than full-speed hunting.
        float gain=delayedPeerFeedback?.25f:4f;
        return new PlayerInput(Math.Clamp(x*gain,-1,1),Math.Clamp(z*gain,-1,1));
    }
}
