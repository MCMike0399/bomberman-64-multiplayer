using BlastCircuit.Testing;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

internal static class InputSendChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
        var press=new PlayerInput(1,0,B:true);var hold=new PlayerInput(0,0,B:true);
        var tracker=new InputSendTracker(1);
        var first=tracker.Capture(press,10,1600);
        Check(first.TotalSentSamples==0&&first.MatchingSentSamples==0,"Capturing/allocating an input does not count as sending it");
        var one=tracker.Complete(first);
        Check(one.TotalSentSamples==1&&!one.ContainsNewSends(press,1,0,2),"One successful input send is insufficient");
        var second=tracker.Capture(press,11,1633);
        Check(second.TotalSentSamples==1&&second.RunStartedAt==1600,"A delayed or failed second send cannot advance the counter");
        var two=tracker.Complete(second);
        Check(two.ContainsNewSends(press,1,0,2)&&two.SentSequence==11,"Two matching completed sends provide the observation");
        Check(!two.ContainsNewSends(press,2,0,2)&&!two.ContainsNewSends(hold,1,0,2)&&!two.ContainsNewSends(press,1,2,2),"Wrong generation/input or old sends cannot satisfy a new nudge");
        var neutral=tracker.Capture(hold,12,1666);
        Check(neutral.MatchingSentSamples==0&&!neutral.ContainsNewSends(press,1,0,2),"Prior movement sends cannot be attributed to a neutral sample");
        var gate=new ObservedInputNudge(press,1,0,1000,5000,200);
        Check(gate.Pressed(null,1500,1),"A stalled producer does not expire an unsampled intent after a guessed pulse duration");
        Check(gate.Pressed(first,1600,1)&&gate.Pressed(one,1601,1)&&gate.Pressed(second,1633,1),"Intent remains until two actual sends complete");
        Check(!gate.Pressed(two,1634,1)&&gate.Decision is {Delivered:true,Reason:"two-sent-samples"},"A late first sample releases on observation, inside its own corridor budget");
        Check(!gate.Pressed(two,1700,1),"Released movement cannot be resurrected by a late frame");
        var pending=new ObservedInputNudge(press,1,0,1000,5000,200);
        Check(!pending.Pressed(null,6000,1)&&pending.Decision is {Delivered:false,Reason:"sender-timeout"},"An unsampled intent eventually cancels without claiming delivery");
        var cutoff=new ObservedInputNudge(press,1,0,1000,5000,200);
        Check(cutoff.Pressed(first,1600,1)&&!cutoff.Pressed(first,1800,1)&&cutoff.Decision is {Delivered:false,Reason:"corridor-cutoff"},"Once sampling starts, the verified corridor bounds the hold");
        var replaced=new ObservedInputNudge(press,1,0,1000,5000,200);
        Check(!replaced.Pressed(two,1640,2)&&replaced.Decision is {Delivered:false,Reason:"session-changed"},"Session replacement cancels the old controller");
        return checks;
    }
}
