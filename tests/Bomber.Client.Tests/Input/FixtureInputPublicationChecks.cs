using System.Text.Json;
using System.Reflection;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.Testing;

internal static class FixtureInputPublicationChecks
{
    internal static int Run()
    {
        int checks=0;void Check(bool result,string text){if(!result)throw new Exception(text);checks++;}
        var publication=new FixtureInputPublication();var session=new Recording();var other=new Recording();
        var press=new PlayerInput(1,0,B:true);var neutral=new PlayerInput(0,0,B:true);
        var before=publication.Capture(session,0);
        using(var owner=publication.Own(session,0))
        {
            Check(owner.Publish(press),"Fixture owner publishes through the existing session path");
            Check(!publication.PublishFrame(before,press),"Frame sampled before acquisition cannot publish while owned");
            var during=publication.Capture(session,0);Check(!publication.PublishFrame(during,press),"Engine publisher cannot overwrite fixture input");
            Check(owner.Publish(neutral),"Owner publishes its real neutral release");owner.Dispose();
            Check(!publication.PublishFrame(before,press),"Frame sampled before acquisition cannot resurrect press after disposal");
            Check(!publication.PublishFrame(during,press),"Frame sampled while owned cannot resurrect press after disposal");
            Check(session.Last==neutral,"Confirmed neutral survives both stale frame interleavings");
            Check(publication.PublishFrame(publication.Capture(session,0),neutral),"Fresh post-lease frames restore ordinary input publication");
            Check(!owner.Publish(press),"Disposed owner cannot publish into a later epoch");
        }
        using(var owner=publication.Own(session,0))
        {
            Check(publication.PublishFrame(publication.Capture(other,0),press),"Ownership never suppresses another session");
            Check(publication.PublishFrame(publication.Capture(session,1),press),"Ownership never suppresses another seat");
            var stale=publication.Capture(session,0);publication.Invalidate();
            Check(!owner.Publish(press)&&!publication.PublishFrame(stale,press),"Session/generation invalidation rejects owner and cached frame together");
        }
        var constructor=typeof(NetworkSession).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Uri),typeof(HttpMessageHandler)],null)!;
        using var handler=new HttpClientHandler();var network=(NetworkSession)constructor.Invoke([new Uri("http://127.0.0.1:1"),handler]);
        try
        {
            var oldFrame=publication.Capture(network,0);using var owner=publication.Own(network,0);
            typeof(NetworkSession).GetField("_connectionGeneration",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(network,1);
            Check(!owner.Publish(press)&&!publication.PublishFrame(oldFrame,press),"A replaced connection generation rejects old owner and old sample");
            publication.Invalidate();Check(publication.PublishFrame(publication.Capture(network,0),neutral),"Fresh generation resumes ordinary publication after root invalidation");
        }
        finally{network.DisposeAsync().AsTask().GetAwaiter().GetResult();}
        Console.WriteLine($"PASS {checks} fixture input-publication checks");return checks;
    }
    private sealed class Recording:ISession
    {
        internal PlayerInput Last;
        public int LocalPlayerCount=>2;public string Status=>"Connected";public string? JoinCode=>null;public bool IsEnded=>false;
        public void Advance(double deltaSeconds){}public void SendInput(int localPlayer,PlayerInput input)=>Last=input;
        public void ReleaseInputs()=>Last=default;public IReadOnlyList<JsonElement> DrainMessages()=>[];public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
